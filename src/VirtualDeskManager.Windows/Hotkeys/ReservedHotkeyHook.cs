using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Hotkeys;

/// <summary>
/// Atrapa las combinaciones que Windows se reserva, antes de que el shell se las quede.
/// </summary>
/// <remarks>
/// <c>RegisterHotKey</c> es la manera correcta de pedir un atajo global, pero Windows se niega a
/// dar las suyas: Ctrl+Esc abre Inicio, Win+E abre el Explorador, y no hay forma de pedirlas por
/// las buenas. Un gancho de bajo nivel las ve <b>antes</b> que el shell, y devolviendo 1 se las
/// traga, que es lo que hacen PowerToys y AutoHotkey.
///
/// Esto es la red, no el mecanismo principal: sólo se usa para las combinaciones que
/// <c>RegisterHotKey</c> ha rechazado. Un gancho de bajo nivel ve <b>todas</b> las teclas que se
/// pulsan en el equipo mientras está puesto, así que cuanto menos tiempo esté y cuanto menos haga,
/// mejor. Con la lista vacía se desengancha solo.
///
/// Tres cosas que hay que tener claras:
///
/// El callback tiene que volver enseguida. Windows desengancha sin avisar a quien tarde más que
/// <c>LowLevelHooksTimeout</c>, unos 300 ms, así que aquí no se hace más que comparar códigos y
/// avisar; ejecutar la acción es cosa de quien escucha, y ya lo hace sin bloquear.
///
/// Con una ventana <b>elevada</b> en primer plano, Windows no deja que un programa sin privilegios
/// le quite las teclas. Vdesk corre como usuario estándar a propósito, así que ahí la combinación
/// vuelve a hacer lo de Windows. No es un fallo que se pueda arreglar sin pedir administrador.
///
/// Y hay combinaciones que no atrapa ningún gancho: <c>Ctrl+Alt+Supr</c> y <c>Win+L</c> las
/// resuelve el sistema por debajo, por seguridad.
/// </remarks>
public sealed class ReservedHotkeyHook : IDisposable
{
    private readonly Win32MessageLoop _loop;
    private readonly IAppLogger _logger;

    /// <summary>El delegado tiene que sobrevivir tanto como el gancho, o Windows llama a memoria liberada.</summary>
    private readonly User32.LowLevelKeyboardProc _callback;

    private readonly object _gate = new();
    private Dictionary<HotkeyCombination, WatchedHotkey> _watched = new();

    private nint _hook;

    /// <summary>La tecla que disparó y sigue pulsada, para no repetir mientras se mantiene.</summary>
    private ushort _held;

    private bool _disposed;

    /// <summary>Lo prepara sin enganchar nada: el gancho se pone cuando <see cref="Watch"/> recibe algo que vigilar.</summary>
    /// <param name="loop">Hilo del bucle de mensajes, donde se instala el gancho y se avisa.</param>
    /// <param name="logger">Diario de diagnóstico, opcional.</param>
    public ReservedHotkeyHook(Win32MessageLoop loop, IAppLogger? logger = null)
    {
        _loop = loop;
        _logger = logger ?? NullAppLogger.Instance;
        _callback = OnKey;
    }

    /// <summary>Qué atajo hay que lanzar y si admite repetición al mantener la tecla.</summary>
    private sealed record WatchedHotkey(string Id, bool AllowRepeat);

    /// <summary>Se dispara en el hilo del bucle de mensajes, igual que WM_HOTKEY.</summary>
    public event EventHandler<string>? Triggered;

    /// <summary>True mientras el gancho está puesto.</summary>
    public bool IsRunning => _hook != 0;

    /// <summary>Cuántas combinaciones se están vigilando.</summary>
    public int WatchedCount
    {
        get
        {
            lock (_gate)
            {
                return _watched.Count;
            }
        }
    }

    /// <summary>
    /// Sustituye la lista de combinaciones vigiladas. Con la lista vacía se desengancha.
    /// </summary>
    public void Watch(IEnumerable<(HotkeyCombination Combination, string Id, bool AllowRepeat)> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        if (_disposed)
        {
            return;
        }

        Dictionary<HotkeyCombination, WatchedHotkey> map = new();

        foreach ((HotkeyCombination combination, string id, bool allowRepeat) in hotkeys)
        {
            if (combination.IsSet)
            {
                map[combination.Normalised] = new WatchedHotkey(id, allowRepeat);
            }
        }

        lock (_gate)
        {
            _watched = map;
        }

        if (map.Count == 0)
        {
            Uninstall();
        }
        else
        {
            Install();
        }
    }

    /// <summary>Deja de vigilar todo y, con ello, quita el gancho.</summary>
    public void Clear() => Watch(Array.Empty<(HotkeyCombination, string, bool)>());

    private void Install()
    {
        if (_hook != 0)
        {
            return;
        }

        try
        {
            _loop.Invoke(() =>
            {
                // Sobre el hilo del bucle: el callback llega a ese hilo, y es el que bombea
                // mensajes. Instalarlo desde otro sitio dejaria el callback en un hilo sin bomba
                // y Windows lo desengancharia a la primera.
                _hook = User32.SetWindowsHookEx(NativeConstants.WH_KEYBOARD_LL, _callback, 0, 0);
            });
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Install)));
            return;
        }

        if (_hook == 0)
        {
            _logger.Warn(
                LogEvents.HotkeyRegistrationFailed,
                "No se ha podido instalar el gancho para las combinaciones reservadas.");

            return;
        }

        _logger.Info("ReservedHotkeyHookStarted", ("Watched", WatchedCount));
    }

    private void Uninstall()
    {
        if (_hook == 0)
        {
            return;
        }

        try
        {
            _loop.Invoke(() =>
            {
                if (_hook != 0)
                {
                    User32.UnhookWindowsHookEx(_hook);
                    _hook = 0;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Uninstall)));
            _hook = 0;
        }

        _logger.Info("ReservedHotkeyHookStopped");
    }

    private nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code != NativeConstants.HC_ACTION)
        {
            return User32.CallNextHookEx(_hook, code, wParam, lParam);
        }

        try
        {
            uint message = (uint)wParam;
            KBDLLHOOKSTRUCT info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            ushort virtualKey = (ushort)info.vkCode;

            // Lo que sintetiza un programa no cuenta. Esta misma aplicacion manda pulsaciones para
            // cambiar de escritorio, y sin esto se dispararia a si misma.
            if ((info.flags & NativeConstants.LLKHF_INJECTED) != 0)
            {
                return User32.CallNextHookEx(_hook, code, wParam, lParam);
            }

            if (message is NativeConstants.WM_KEYUP or NativeConstants.WM_SYSKEYUP)
            {
                if (virtualKey == _held)
                {
                    _held = 0;
                }

                return User32.CallNextHookEx(_hook, code, wParam, lParam);
            }

            if (message is not (NativeConstants.WM_KEYDOWN or NativeConstants.WM_SYSKEYDOWN))
            {
                return User32.CallNextHookEx(_hook, code, wParam, lParam);
            }

            if (IsModifier(virtualKey))
            {
                // Un modificador suelto no es una combinación, y tragárselo dejaría al sistema sin
                // enterarse de que está pulsado.
                return User32.CallNextHookEx(_hook, code, wParam, lParam);
            }

            HotkeyCombination pressed = new(CurrentModifiers(), (HotkeyKey)virtualKey);

            WatchedHotkey? watched;

            lock (_gate)
            {
                _watched.TryGetValue(pressed.Normalised, out watched);
            }

            if (watched is null)
            {
                return User32.CallNextHookEx(_hook, code, wParam, lParam);
            }

            // Mantener la tecla pulsada genera una riada de WM_KEYDOWN. RegisterHotKey lo evita
            // con MOD_NOREPEAT; aqui hay que hacerlo a mano o un atajo mantenido medio segundo se
            // lanzaria treinta veces.
            if (!watched.AllowRepeat && _held == virtualKey)
            {
                return 1;
            }

            _held = virtualKey;

            // Quien escucha devuelve en el acto, igual que con WM_HOTKEY: aqui no se puede tardar.
            Triggered?.Invoke(this, watched.Id);

            // Tragada: es justo lo que hace que Ctrl+Esc deje de abrir el menu Inicio.
            return 1;
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(OnKey)));
            return User32.CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    private static bool IsModifier(ushort virtualKey) =>
        virtualKey is NativeConstants.VK_LWIN
            or NativeConstants.VK_RWIN
            or NativeConstants.VK_LCONTROL
            or NativeConstants.VK_RCONTROL
            or NativeConstants.VK_CONTROL
            or NativeConstants.VK_LMENU
            or NativeConstants.VK_RMENU
            or NativeConstants.VK_MENU
            or NativeConstants.VK_LSHIFT
            or NativeConstants.VK_RSHIFT
            or NativeConstants.VK_SHIFT;

    /// <summary>El bit alto dice si la tecla está abajo ahora mismo.</summary>
    private static HotkeyModifiers CurrentModifiers()
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;

        if (Down(NativeConstants.VK_CONTROL))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Down(NativeConstants.VK_MENU))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Down(NativeConstants.VK_SHIFT))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Down(NativeConstants.VK_LWIN) || Down(NativeConstants.VK_RWIN))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool Down(ushort virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>Quita el gancho, pase lo que pase, y ya no vuelve a ponerlo.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Pase lo que pase: un gancho que se quede puesto tragandose teclas deja al usuario sin
        // teclado en todo el equipo.
        Uninstall();
    }
}
