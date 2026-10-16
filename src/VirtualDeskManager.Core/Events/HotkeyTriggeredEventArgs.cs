using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Events;

/// <summary>Se ha pulsado uno de los atajos globales registrados.</summary>
public sealed class HotkeyTriggeredEventArgs : EventArgs
{
    /// <summary>Crea los argumentos con la hora UTC actual.</summary>
    public HotkeyTriggeredEventArgs(HotkeyDefinition hotkey)
    {
        Hotkey = hotkey;
        TimestampUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>El atajo pulsado, con la orden que tiene asignada.</summary>
    public HotkeyDefinition Hotkey { get; }

    /// <summary>Cuándo se atendió la pulsación.</summary>
    public DateTimeOffset TimestampUtc { get; }
}
