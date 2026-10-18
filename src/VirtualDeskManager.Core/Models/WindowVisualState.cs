namespace VirtualDeskManager.Core.Models;

/// <summary>Show state of a window, normalized away from the SW_* constants.</summary>
public enum WindowVisualState
{
    /// <summary>Restored: neither minimized nor maximized.</summary>
    Normal,
    /// <summary>Minimized to the taskbar.</summary>
    Minimized,
    /// <summary>Maximized on its monitor.</summary>
    Maximized
}
