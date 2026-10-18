namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Immutable snapshot of a top level window.
/// </summary>
/// <remarks>
/// <see cref="Handle"/> must never be used as a persistent identifier: the HWND is recycled by
/// Windows once the window is destroyed (spec section 9). Persist process name / class instead.
/// </remarks>
public sealed class WindowInfo
{
    /// <summary>The window handle (HWND). Valid only while the window exists.</summary>
    public nint Handle { get; init; }

    /// <summary>Identifier of the owning process.</summary>
    public uint ProcessId { get; init; }

    /// <summary>Executable file name including extension, e.g. <c>notepad.exe</c>.</summary>
    public string ProcessName { get; init; } = "";

    /// <summary>Full path of the executable. Empty when it could not be read.</summary>
    public string ExecutablePath { get; init; } = "";

    /// <summary>Caption text at the moment of the snapshot.</summary>
    public string Title { get; init; } = "";

    /// <summary>Win32 window class name. Stable across runs, so rules can match on it.</summary>
    public string ClassName { get; init; } = "";

    /// <summary>Desktop the window is on, or null when it is not known.</summary>
    public Guid? VirtualDesktopId { get; init; }

    /// <summary>Whether the window has the visible style. A window on another desktop still counts as visible.</summary>
    public bool IsVisible { get; init; }

    /// <summary>Whether the window is always on top.</summary>
    public bool IsTopMost { get; init; }

    /// <summary>Whether the window is minimized.</summary>
    public bool IsMinimized { get; init; }

    /// <summary>Whether the window is maximized.</summary>
    public bool IsMaximized { get; init; }

    /// <summary>Outer rectangle in screen pixels, or <see cref="WindowBounds.Empty"/> when it could not be read.</summary>
    public WindowBounds Bounds { get; init; }

    /// <summary>Handle formatted the way it is shown in the dashboard (spec section 29).</summary>
    public string HandleText => "0x" + Handle.ToString("X16");

    /// <summary>Handle, process and title for the log.</summary>
    public override string ToString() => $"{HandleText} {ProcessName} \"{Title}\"";
}
