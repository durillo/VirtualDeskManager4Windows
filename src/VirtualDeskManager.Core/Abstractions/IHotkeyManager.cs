using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>Registration outcome of a single hotkey, surfaced by the Hotkeys screen (spec section 6).</summary>
public sealed class HotkeyRegistration
{
    /// <summary>El atajo tal como está en la configuración.</summary>
    public required HotkeyDefinition Definition { get; init; }

    /// <summary>Si el atajo está activo, sea porque Windows lo aceptó o por el gancho de teclado (<see cref="ViaHook"/>).</summary>
    public bool IsRegistered { get; init; }

    /// <summary>Reason why Windows rejected the combination, usually "already in use".</summary>
    public string? Error { get; init; }

    /// <summary>
    /// El atajo funciona porque se atrapa con un gancho de teclado, no porque Windows lo haya dado.
    /// </summary>
    /// <remarks>
    /// Merece decirse en la lista: un atajo por gancho deja de funcionar cuando la ventana de
    /// delante es de un programa elevado, y saberlo ahorra media hora de desconcierto.
    /// </remarks>
    public bool ViaHook { get; init; }
}

/// <summary>Registra los atajos de teclado globales y avisa cuando se pulsan.</summary>
public interface IHotkeyManager
{
    /// <summary>Registers the hotkey. Returns false when Windows rejects it; never throws.</summary>
    bool Register(HotkeyDefinition hotkey);

    /// <summary>Suelta el atajo con ese identificador, si estaba registrado.</summary>
    void Unregister(string id);

    /// <summary>Suelta todos los atajos registrados.</summary>
    void UnregisterAll();

    /// <summary>Resultado del registro de cada atajo, para enseñarlo en la pantalla de atajos.</summary>
    IReadOnlyList<HotkeyRegistration> Registrations { get; }

    /// <summary>Salta cuando el usuario pulsa uno de los atajos registrados.</summary>
    event EventHandler<HotkeyTriggeredEventArgs>? HotkeyTriggered;
}
