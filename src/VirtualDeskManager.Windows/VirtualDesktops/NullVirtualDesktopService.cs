using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Windows.VirtualDesktops;

/// <summary>
/// Fallback used when no virtual desktop backend could be initialised. Every capability is
/// false and every operation answers <see cref="OperationStatus.Unsupported"/>, so the
/// application keeps running even if Windows changes underneath (spec sections 14 and 44).
/// </summary>
public sealed class NullVirtualDesktopService : IVirtualDesktopService
{
    private readonly string _reason;

    /// <summary>Creates the fallback.</summary>
    /// <param name="reason">What every refused operation reports. Defaults to a generic message.</param>
    public NullVirtualDesktopService(string? reason = null) =>
        _reason = reason ?? "No virtual desktop backend is available.";

    /// <summary>Always "None".</summary>
    public string BackendName => "None";

    /// <summary>Nothing is available.</summary>
    public VirtualDesktopCapabilities Capabilities => VirtualDesktopCapabilities.None;

    /// <summary>Always true: with no backend every window is treated as being here.</summary>
    public bool IsWindowOnCurrentDesktop(nint hwnd) => true;

    /// <summary>Always null: the desktop cannot be known.</summary>
    public Guid? GetWindowDesktopId(nint hwnd) => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult MoveWindowToDesktop(nint hwnd, Guid desktopId) => OperationResult.NotSupported(_reason);

    /// <summary>Always empty.</summary>
    public IReadOnlyList<VirtualDesktopInfo> GetDesktops() => Array.Empty<VirtualDesktopInfo>();

    /// <summary>Always null.</summary>
    public VirtualDesktopInfo? GetCurrentDesktop() => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SwitchToDesktop(Guid desktopId) => OperationResult.NotSupported(_reason);

    /// <summary>Always null: no desktop is created.</summary>
    public VirtualDesktopInfo? CreateDesktop() => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult RemoveDesktop(Guid desktopId) => OperationResult.NotSupported(_reason);

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SetDesktopName(Guid desktopId, string name) => OperationResult.NotSupported(_reason);

    /// <summary>Always null: it cannot be known.</summary>
    public bool? IsWindowPinned(nint hwnd) => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SetWindowPinned(nint hwnd, bool pinned) => OperationResult.NotSupported(_reason);

    /// <summary>Always null: it cannot be known.</summary>
    public bool? IsApplicationPinned(nint hwnd) => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SetApplicationPinned(nint hwnd, bool pinned) => OperationResult.NotSupported(_reason);
}
