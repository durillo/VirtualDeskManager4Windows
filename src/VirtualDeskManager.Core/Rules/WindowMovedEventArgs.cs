using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>A rule has sent a window to a desktop the user is not looking at.</summary>
public sealed class WindowMovedEventArgs : EventArgs
{
    /// <summary>Creates the arguments for a window that has just been moved.</summary>
    public WindowMovedEventArgs(WindowInfo window, VirtualDesktopInfo desktop)
    {
        Window = window;
        Desktop = desktop;
    }

    /// <summary>The window that was moved.</summary>
    public WindowInfo Window { get; }

    /// <summary>The desktop it was sent to, which is not the one on screen.</summary>
    public VirtualDesktopInfo Desktop { get; }
}
