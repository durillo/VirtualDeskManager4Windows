using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Hotkeys;

/// <summary>
/// Global hotkeys through RegisterHotKey (spec section 6).
/// A combination Windows refuses is recorded and shown in the UI; it never stops the application.
/// </summary>
public sealed class HotkeyManager : IHotkeyManager, IDisposable
{
    private const int FirstNativeId = 0xB000;

    private readonly Win32MessageLoop _loop;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<int, HotkeyDefinition> _byNativeId = new();

    /// <summary>
    /// Los identificadores nativos de cada atajo. Son varios porque un atajo puede tener dos
    /// combinaciones, y Windows registra cada combinación por separado.
    /// </summary>
    private readonly Dictionary<string, List<int>> _nativeIdsByHotkeyId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HotkeyRegistration> _registrations = new();

    /// <summary>
    /// Las combinaciones que Windows ha rechazado y hay que atrapar por gancho.
    /// </summary>
    /// <remarks>
    /// Se juntan todas y se le pasan al gancho de una vez al terminar de registrar, en vez de ir
    /// enganchando y desenganchando por cada atajo: mientras el gancho esta puesto, todas las
    /// teclas del equipo pasan por el, asi que cuantas menos veces se toque, mejor.
    /// </remarks>
    private readonly List<(HotkeyCombination Combination, string Id, bool AllowRepeat)> _hooked = new();

    private readonly ReservedHotkeyHook _hook;

    /// <summary>Si se intenta atrapar por gancho lo que Windows no da por las buenas.</summary>
    public bool InterceptReserved { get; set; } = true;

    private int _nextNativeId = FirstNativeId;
    private bool _disposed;

    /// <summary>
    /// Se engancha al bucle de mensajes, por donde llegan las pulsaciones de <c>WM_HOTKEY</c>, y prepara
    /// el gancho de teclado para las combinaciones que Windows se reserva.
    /// </summary>
    public HotkeyManager(Win32MessageLoop loop, IAppLogger? logger = null)
    {
        _loop = loop;
        _logger = logger ?? NullAppLogger.Instance;
        _loop.MessageReceived += OnMessageReceived;

        _hook = new ReservedHotkeyHook(loop, _logger);
        _hook.Triggered += OnHookTriggered;
    }

    /// <summary>Raised on the message loop thread; handlers must not block.</summary>
    public event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;

    /// <inheritdoc />
    public IReadOnlyList<HotkeyRegistration> Registrations
    {
        get
        {
            lock (_gate)
            {
                return _registrations.ToList();
            }
        }
    }

    /// <inheritdoc />
    public bool Register(HotkeyDefinition hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        if (_disposed)
        {
            return false;
        }

        if (!hotkey.Enabled)
        {
            Record(hotkey, false, "Disabled in hotkeys.json.");
            return false;
        }

        if (!hotkey.IsValid)
        {
            Record(hotkey, false, "Invalid combination: a key plus at least one of Ctrl, Alt or Shift is required.");
            _logger.Warn(LogEvents.HotkeyRegistrationFailed, ("Id", hotkey.Id), ("Reason", "invalid"));
            return false;
        }

        // Re-registering the same id replaces the previous binding.
        Unregister(hotkey.Id);

        // La principal manda: si falla, el atajo falla. La segunda es un extra, y que Windows la
        // rechace no tiene por qué llevarse por delante la que sí funciona.
        if (!TryRegisterOne(hotkey, hotkey.Modifiers, hotkey.Key, out string? error, out bool viaHook))
        {
            Record(hotkey, false, error);
            return false;
        }

        // Fuera del && de abajo: cortocircuitado, el compilador no da por asignado lo que sale de
        // una llamada que puede no hacerse.
        string? alternateError = null;
        bool alternateViaHook = false;

        if (hotkey.HasAlternate
            && !TryRegisterOne(
                hotkey,
                hotkey.AlternateModifiers,
                hotkey.AlternateKey,
                out alternateError,
                out alternateViaHook))
        {
            // Registrada la principal y rechazada la segunda: el atajo funciona, pero el usuario
            // tiene que poder enterarse de que sólo lo hace de una de las dos maneras.
            Record(hotkey, true, alternateError, viaHook);

            _logger.Warn(
                LogEvents.HotkeyRegistrationFailed,
                alternateError ?? "",
                ("Id", hotkey.Id),
                ("Shortcut", hotkey.AlternateDisplayText),
                ("Which", "alternate"));

            return true;
        }

        Record(hotkey, true, null, viaHook || alternateViaHook);

        _logger.Info(
            LogEvents.HotkeyRegistered,
            ("Id", hotkey.Id),
            ("Shortcut", hotkey.DisplayText),
            ("Command", hotkey.Command.Type));

        return true;
    }

    /// <summary>Registra una de las combinaciones de un atajo.</summary>
    private bool TryRegisterOne(
        HotkeyDefinition hotkey,
        HotkeyModifiers combinationModifiers,
        HotkeyKey key,
        out string? error,
        out bool viaHook)
    {
        error = null;
        viaHook = false;

        int nativeId;

        lock (_gate)
        {
            nativeId = _nextNativeId++;
        }

        uint modifiers = (uint)(combinationModifiers & ~HotkeyModifiers.NoRepeat);

        if (!hotkey.AllowRepeat)
        {
            modifiers |= NativeConstants.MOD_NOREPEAT;
        }

        bool registered = false;
        int lastError = 0;

        try
        {
            _loop.Invoke(() =>
            {
                registered = User32.RegisterHotKey(_loop.Handle, nativeId, modifiers, (uint)key);

                if (!registered)
                {
                    lastError = Marshal.GetLastWin32Error();
                }
            });
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.Error(LogEvents.HotkeyRegistrationFailed, ex, ("Id", hotkey.Id));
            return false;
        }

        if (!registered)
        {
            error = lastError == NativeConstants.ERROR_HOTKEY_ALREADY_REGISTERED
                ? "The combination is already used by another application."
                : $"Windows rejected the combination (error {lastError}).";

            // Windows no la da por las buenas: se atrapa con el gancho, que ve las teclas antes
            // que el shell. Es lo que hace que Ctrl+Esc pueda dejar de abrir el menu Inicio.
            if (InterceptReserved)
            {
                lock (_gate)
                {
                    _hooked.Add((new HotkeyCombination(combinationModifiers, key), hotkey.Id, hotkey.AllowRepeat));
                }

                _logger.Info(
                    LogEvents.HotkeyRegistered,
                    ("Id", hotkey.Id),
                    ("Shortcut", HotkeyDefinition.Format(combinationModifiers, key)),
                    ("Via", "hook"),
                    ("WindowsSaid", lastError));

                viaHook = true;
                return true;
            }

            _logger.Warn(
                LogEvents.HotkeyRegistrationFailed,
                error,
                ("Id", hotkey.Id),
                ("Shortcut", HotkeyDefinition.Format(combinationModifiers, key)),
                ("Error", lastError));

            return false;
        }

        lock (_gate)
        {
            _byNativeId[nativeId] = hotkey;

            if (!_nativeIdsByHotkeyId.TryGetValue(hotkey.Id, out List<int>? ids))
            {
                ids = new List<int>(2);
                _nativeIdsByHotkeyId[hotkey.Id] = ids;
            }

            ids.Add(nativeId);
        }

        return true;
    }

    /// <inheritdoc />
    public void Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || _disposed)
        {
            return;
        }

        lock (_gate)
        {
            // También las que atrapaba el gancho, o una combinación borrada seguiría tragándose
            // teclas sin que exista ya el atajo al que iban.
            _hooked.RemoveAll(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        List<int> nativeIds;

        lock (_gate)
        {
            if (!_nativeIdsByHotkeyId.TryGetValue(id, out List<int>? found))
            {
                _registrations.RemoveAll(r => string.Equals(r.Definition.Id, id, StringComparison.OrdinalIgnoreCase));
                return;
            }

            nativeIds = found;
            _nativeIdsByHotkeyId.Remove(id);

            foreach (int nativeId in nativeIds)
            {
                _byNativeId.Remove(nativeId);
            }

            _registrations.RemoveAll(r => string.Equals(r.Definition.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        // Las dos combinaciones, si las hay: dejarse una sin quitar dejaría a Windows mandando
        // pulsaciones de un atajo que ya no existe.
        foreach (int nativeId in nativeIds)
        {
            UnregisterNative(nativeId, id);
        }
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        List<KeyValuePair<string, List<int>>> pairs;

        lock (_gate)
        {
            pairs = _nativeIdsByHotkeyId.ToList();
            _nativeIdsByHotkeyId.Clear();
            _byNativeId.Clear();
            _registrations.Clear();
            _hooked.Clear();
        }

        foreach (KeyValuePair<string, List<int>> pair in pairs)
        {
            foreach (int nativeId in pair.Value)
            {
                UnregisterNative(nativeId, pair.Key);
            }
        }
    }

    private void UnregisterNative(int nativeId, string hotkeyId)
    {
        try
        {
            _loop.Invoke(() => { User32.UnregisterHotKey(_loop.Handle, nativeId); });
            _logger.Debug(LogEvents.HotkeyUnregistered, ("Id", hotkeyId));
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(UnregisterNative)), ("Id", hotkeyId));
        }
    }


    /// <summary>
    /// Le pasa al gancho todo lo que Windows haya rechazado en esta tanda.
    /// </summary>
    /// <remarks>
    /// Hay que llamarlo despues de registrar todos los atajos. Lo llama RegisterHotkeys de la
    /// aplicacion; registrar uno suelto desde el editor tambien lo llama, con lo que la lista se
    /// rehace entera y no queda ningun resto de una combinacion que ya no existe.
    /// </remarks>
    public void ApplyHookedCombinations()
    {
        List<(HotkeyCombination, string, bool)> pending;

        lock (_gate)
        {
            pending = _hooked.Select(one => (one.Combination, one.Id, one.AllowRepeat)).ToList();
        }

        _hook.Watch(pending);
    }

    /// <summary>Llega en el hilo del bucle, igual que WM_HOTKEY, y no puede bloquear.</summary>
    private void OnHookTriggered(object? sender, string hotkeyId)
    {
        HotkeyDefinition? hotkey;

        lock (_gate)
        {
            hotkey = _registrations
                .FirstOrDefault(r => string.Equals(r.Definition.Id, hotkeyId, StringComparison.OrdinalIgnoreCase))
                ?.Definition;
        }

        if (hotkey is null)
        {
            return;
        }

        _logger.Info(
            LogEvents.HotkeyTriggered,
            ("Id", hotkey.Id),
            ("Shortcut", hotkey.DisplayText),
            ("Command", hotkey.Command.Type),
            ("Via", "hook"));

        HotkeyTriggered?.Invoke(this, new HotkeyTriggeredEventArgs(hotkey));
    }

    private void Record(HotkeyDefinition hotkey, bool registered, string? error, bool viaHook = false)
    {
        lock (_gate)
        {
            _registrations.RemoveAll(r => string.Equals(r.Definition.Id, hotkey.Id, StringComparison.OrdinalIgnoreCase));

            _registrations.Add(new HotkeyRegistration
            {
                Definition = hotkey,
                IsRegistered = registered,
                Error = error,
                ViaHook = viaHook
            });
        }
    }

    private void OnMessageReceived(object? sender, Win32Message message)
    {
        if (message.Message != NativeConstants.WM_HOTKEY)
        {
            return;
        }

        int nativeId = (int)message.WParam;
        HotkeyDefinition? definition;

        lock (_gate)
        {
            _byNativeId.TryGetValue(nativeId, out definition);
        }

        if (definition is null)
        {
            return;
        }

        _logger.Info(
            LogEvents.HotkeyTriggered,
            ("Id", definition.Id),
            ("Shortcut", definition.DisplayText),
            ("Command", definition.Command.Type));

        HotkeyTriggered?.Invoke(this, new HotkeyTriggeredEventArgs(definition));
    }

    /// <summary>Suelta todos los atajos, quita el gancho de teclado y se desengancha del bucle.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();

        // El gancho, pase lo que pase: uno que se quede puesto tragandose teclas deja al usuario
        // sin teclado en todo el equipo.
        _hook.Triggered -= OnHookTriggered;
        _hook.Dispose();

        _loop.MessageReceived -= OnMessageReceived;
        _disposed = true;
    }
}
