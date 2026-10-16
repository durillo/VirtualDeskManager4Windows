using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Events;

/// <summary>Which native window event was seen.</summary>
public enum WindowEventKind
{
    /// <summary>A window was created (<c>EVENT_OBJECT_CREATE</c>); it may not be visible yet.</summary>
    Created,
    /// <summary>A window became visible (<c>EVENT_OBJECT_SHOW</c>).</summary>
    Shown,
    /// <summary>A window took the foreground (<c>EVENT_SYSTEM_FOREGROUND</c>).</summary>
    ForegroundChanged,
    /// <summary>A window was destroyed (<c>EVENT_OBJECT_DESTROY</c>); it can no longer be inspected.</summary>
    Destroyed
}

/// <summary>
/// Raised by <see cref="Abstractions.IWindowEventService"/> after the native callback has already
/// returned, never from inside the Win32 callback itself (spec section 11).
/// </summary>
public sealed class WindowDetectedEventArgs : EventArgs
{
    /// <summary>Creates the arguments, stamped with the current UTC time.</summary>
    public WindowDetectedEventArgs(nint handle, WindowEventKind kind, WindowInfo? window)
    {
        Handle = handle;
        Kind = kind;
        Window = window;
        TimestampUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The window's HWND. Always set, even when <see cref="Window"/> is null.</summary>
    public nint Handle { get; }

    /// <summary>Which event this is.</summary>
    public WindowEventKind Kind { get; }

    /// <summary>Null when the window vanished before it could be inspected, or on destroy.</summary>
    public WindowInfo? Window { get; }

    /// <summary>When the arguments were built, which is shortly after the native event.</summary>
    public DateTimeOffset TimestampUtc { get; }
}
