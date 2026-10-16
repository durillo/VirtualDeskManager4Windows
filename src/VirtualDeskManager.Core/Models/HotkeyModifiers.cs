namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Hotkey modifiers. Values match the Win32 MOD_* constants on purpose so the Windows layer
/// can cast directly, but the enum itself carries no dependency on Win32.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0x0000,
    /// <summary>Alt (MOD_ALT).</summary>
    Alt = 0x0001,
    /// <summary>Ctrl (MOD_CONTROL). Ctrl+Alt is what AltGr sends on most European layouts.</summary>
    Control = 0x0002,
    /// <summary>Shift (MOD_SHIFT).</summary>
    Shift = 0x0004,

    /// <summary>Reserved by the shell. Avoid it in defaults (spec section 55).</summary>
    Windows = 0x0008,

    /// <summary>MOD_NOREPEAT: do not fire again while the key is held down (spec section 6).</summary>
    NoRepeat = 0x4000
}
