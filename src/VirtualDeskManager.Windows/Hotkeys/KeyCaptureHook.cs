using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Hotkeys;

/// <summary>Una combinación tal y como la vio el hook.</summary>
public sealed class KeyCapturedEventArgs : EventArgs
{
    /// <summary>Guarda la combinación leída.</summary>
    public KeyCapturedEventArgs(HotkeyModifiers modifiers, HotkeyKey key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    /// <summary>Los modificadores que estaban pulsados, tecla Windows incluida.</summary>
    public HotkeyModifiers Modifiers { get; }

    /// <summary>La tecla principal de la combinación.</summary>
    public HotkeyKey Key { get; }
}

/// <summary>
/// Lee combinaciones de teclas antes de que Windows se las quede.
/// </summary>
/// <remarks>
/// Existe por una razón concreta: <b>sin esto la tecla Windows no se puede asignar</b>. El shell
/// se reserva casi todas las combinaciones con ella —Win+E, Win+R, Win+D y muchísimas más— y las
/// consume antes de que lleguen a ninguna ventana, de modo que un manejador de teclado normal de
/// WPF no llega a enterarse de que se han pulsado. Un hook de bajo nivel las ve primero.
///
/// Mientras está capturando se traga las teclas, para que pulsar Win+E dentro del editor asigne
/// el atajo en vez de abrir el Explorador.
///
/// Dos cosas hay que tener muy claras con esto:
///
/// El callback debe volver enseguida. Windows desengancha sin avisar a quien tarde más que
/// <c>LowLevelHooksTimeout</c>, unos 300 ms, así que aquí no se hace más que mirar códigos.
///
/// Y mientras el hook está puesto el teclado del equipo entero pasa por él. Por eso captura sólo
/// cuando alguien se lo pide expresamente con <see cref="Start"/>, y por eso
/// <see cref="Dispose"/> lo quita pase lo que pase: un hook que se quede puesto tragándose teclas
/// deja al usuario sin teclado.
/// </remarks>
public sealed class KeyCaptureHook : IDisposable
{
    private readonly IAppLogger _logger;

    /// <summary>
    /// El delegado tiene que sobrevivir tanto como el hook.
    /// </summary>
    /// <remarks>
    /// Sin guardarlo en un campo, el recolector se lo lleva mientras Windows sigue teniendo su
    /// dirección, y la siguiente tecla que se pulse llama a memoria liberada.
    /// </remarks>
    private readonly User32.LowLevelKeyboardProc _callback;

    private nint _hook;
    private bool _capturing;
    private bool _disposed;

    /// <summary>Lo prepara sin instalar nada: el hook se pone en el primer <see cref="Start"/>.</summary>
    public KeyCaptureHook(IAppLogger? logger = null)
    {
        _logger = logger ?? NullAppLogger.Instance;
        _callback = OnKey;
    }

    /// <summary>Se dispara con cada combinación completa, en el hilo que instaló el hook.</summary>
    public event EventHandler<KeyCapturedEventArgs>? KeyCaptured;

    /// <summary>True mientras se está quedando con las teclas.</summary>
    public bool IsCapturing => _capturing;

    /// <summary>
    /// Empieza a capturar. Instala el hook la primera vez que hace falta.
    /// </summary>
    /// <returns>False si Windows no dejó instalarlo; entonces el editor sigue con lo de siempre.</returns>
    public bool Start()
    {
        if (_disposed)
        {
            return false;
        }

        if (_hook == 0)
        {
            try
            {
                // Módulo cero e hilo cero: un hook global, que es lo que hace falta para ver las
                // teclas que el shell se queda.
                _hook = User32.SetWindowsHookEx(NativeConstants.WH_KEYBOARD_LL, _callback, 0, 0);
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Start)));
                return false;
            }

            if (_hook == 0)
            {
                _logger.Warn(LogEvents.NativeApiError, "SetWindowsHookEx no instalo el hook de captura.");
                return false;
            }
        }

        _capturing = true;
        return true;
    }

    /// <summary>Deja de capturar, sin quitar el hook: volver a capturar es inmediato.</summary>
    public void Stop() => _capturing = false;

    private nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code != NativeConstants.HC_ACTION || !_capturing)
        {
            return User32.CallNextHookEx(_hook, code, wParam, lParam);
        }

        try
        {
            uint message = (uint)wParam;

            if (message is not (NativeConstants.WM_KEYDOWN or NativeConstants.WM_SYSKEYDOWN))
            {
                // Las sueltas se dejan pasar: tragárselas descuadra el estado de los
                // modificadores en el resto del sistema.
                return User32.CallNextHookEx(_hook, code, wParam, lParam);
            }

            KBDLLHOOKSTRUCT info = System.Runtime.InteropServices.Marshal
                .PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            ushort virtualKey = (ushort)info.vkCode;

            if (IsModifier(virtualKey))
            {
                // Un modificador solo no es una combinación todavía, y hay que dejarlo pasar o
                // el sistema no se entera de que está pulsado.
                return User32.CallNextHookEx(_hook, code, wParam, lParam);
            }

            HotkeyModifiers modifiers = CurrentModifiers();

            KeyCaptured?.Invoke(this, new KeyCapturedEventArgs(modifiers, (HotkeyKey)virtualKey));

            // Tragada: si no, pulsar Win+E dentro del editor abriría el Explorador además de
            // asignar el atajo.
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
            or NativeConstants.VK_LMENU
            or NativeConstants.VK_RMENU
            or NativeConstants.VK_LSHIFT
            or NativeConstants.VK_RSHIFT;

    /// <summary>Qué modificadores están pulsados ahora mismo, incluida la tecla Windows.</summary>
    private static HotkeyModifiers CurrentModifiers()
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;

        if (IsDown(NativeConstants.VK_LCONTROL) || IsDown(NativeConstants.VK_RCONTROL))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(NativeConstants.VK_LMENU) || IsDown(NativeConstants.VK_RMENU))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(NativeConstants.VK_LSHIFT) || IsDown(NativeConstants.VK_RSHIFT))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(NativeConstants.VK_LWIN) || IsDown(NativeConstants.VK_RWIN))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(ushort virtualKey) =>
        (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>Quita el hook y deja de capturar. Se puede llamar más de una vez.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capturing = false;

        if (_hook == 0)
        {
            return;
        }

        try
        {
            User32.UnhookWindowsHookEx(_hook);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Dispose)));
        }

        _hook = 0;
    }
}
