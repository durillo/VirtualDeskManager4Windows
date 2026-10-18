using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.VirtualDesktops.Public;

/// <summary>
/// Stable backend built exclusively on the documented Microsoft interface (spec section 13).
/// No internal COM interface is allowed in this file.
/// </summary>
/// <remarks>
/// The public API can read the desktop of any window, but it can only move windows that belong
/// to the calling process: for anything else Windows answers E_ACCESSDENIED, which is reported
/// as <see cref="OperationStatus.AccessDenied"/> instead of being hidden. Moving foreign windows
/// needs the advanced backend of milestone 3.
/// </remarks>
public sealed class PublicVirtualDesktopService : IVirtualDesktopService, IDisposable
{
    private readonly IAppLogger _logger;
    private readonly object _gate = new();

    private IVirtualDesktopManager? _manager;
    private bool _disposed;

    private PublicVirtualDesktopService(IVirtualDesktopManager manager, IAppLogger logger)
    {
        _manager = manager;
        _logger = logger;
    }

    /// <summary>Always "Public Windows API".</summary>
    public string BackendName => "Public Windows API";

    /// <summary>Only reading the desktop of a window and moving this application's own windows.</summary>
    public VirtualDesktopCapabilities Capabilities { get; } = new()
    {
        CanGetWindowDesktop = true,
        CanMoveWindow = true,
        CanEnumerateDesktops = false,
        CanSwitchDesktop = false,
        CanCreateDesktop = false,
        CanRemoveDesktop = false,
        CanPinWindow = false,
        CanPinApplication = false
    };

    /// <summary>Creates the backend, or returns null when the COM object is not available.</summary>
    public static PublicVirtualDesktopService? TryCreate(IAppLogger? logger = null)
    {
        IAppLogger log = logger ?? NullAppLogger.Instance;

        try
        {
            Type? type = Type.GetTypeFromCLSID(VirtualDesktopClsids.VirtualDesktopManager);

            if (type is null)
            {
                log.Warn(LogEvents.DesktopBackendUnavailable, "CLSID_VirtualDesktopManager is not registered.");
                return null;
            }

            if (Activator.CreateInstance(type) is not IVirtualDesktopManager manager)
            {
                log.Warn(LogEvents.DesktopBackendUnavailable, "IVirtualDesktopManager could not be obtained.");
                return null;
            }

            return new PublicVirtualDesktopService(manager, log);
        }
        catch (Exception ex)
        {
            log.Error(LogEvents.DesktopBackendUnavailable, ex);
            return null;
        }
    }

    /// <summary>Whether the window is on the desktop on screen.</summary>
    /// <remarks>When the backend cannot tell, the answer is true, which never triggers a move.</remarks>
    public bool IsWindowOnCurrentDesktop(nint hwnd)
    {
        IVirtualDesktopManager? manager = GetManager();

        if (manager is null || hwnd == 0)
        {
            // Unknown is reported as "yes": it is the answer that keeps the UI honest when the
            // backend cannot tell, and it never triggers a move.
            return true;
        }

        try
        {
            int hr = manager.IsWindowOnCurrentVirtualDesktop(hwnd, out int onCurrent);

            if (hr != NativeConstants.S_OK)
            {
                LogHResult(nameof(IsWindowOnCurrentDesktop), hwnd, hr);
                return true;
            }

            return onCurrent != 0;
        }
        catch (COMException ex)
        {
            _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(IsWindowOnCurrentDesktop)));
            return true;
        }
    }

    /// <inheritdoc />
    public Guid? GetWindowDesktopId(nint hwnd)
    {
        IVirtualDesktopManager? manager = GetManager();

        if (manager is null || hwnd == 0)
        {
            return null;
        }

        try
        {
            int hr = manager.GetWindowDesktopId(hwnd, out Guid desktopId);

            if (hr != NativeConstants.S_OK)
            {
                LogHResult(nameof(GetWindowDesktopId), hwnd, hr);
                return null;
            }

            return desktopId == Guid.Empty ? null : desktopId;
        }
        catch (COMException ex)
        {
            _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(GetWindowDesktopId)));
            return null;
        }
    }

    /// <summary>Moves a window to another desktop.</summary>
    /// <remarks>
    /// Only works for windows of this process. For any other window Windows refuses, and the answer is
    /// <see cref="OperationStatus.AccessDenied"/>.
    /// </remarks>
    public OperationResult MoveWindowToDesktop(nint hwnd, Guid desktopId)
    {
        IVirtualDesktopManager? manager = GetManager();

        if (manager is null)
        {
            return OperationResult.NotSupported("The virtual desktop backend is not available.");
        }

        if (hwnd == 0 || !User32.IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        if (desktopId == Guid.Empty)
        {
            return OperationResult.Fail("The target desktop identifier is empty.");
        }

        try
        {
            Guid target = desktopId;
            int hr = manager.MoveWindowToDesktop(hwnd, ref target);

            if (hr == NativeConstants.S_OK)
            {
                _logger.Info(
                    LogEvents.DesktopOperation,
                    ("Operation", nameof(MoveWindowToDesktop)),
                    ("Window", hwnd),
                    ("Desktop", desktopId));

                return OperationResult.Ok();
            }

            LogHResult(nameof(MoveWindowToDesktop), hwnd, hr);

            return hr == NativeConstants.E_ACCESSDENIED
                ? OperationResult.Denied(
                    "The public API can only move windows owned by this application. Moving other windows needs the advanced backend.")
                : OperationResult.Fail($"MoveWindowToDesktop failed (HRESULT 0x{hr:X8}).");
        }
        catch (COMException ex)
        {
            _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(MoveWindowToDesktop)));
            return OperationResult.Fail(ex.Message);
        }
    }

    // The documented interface offers three operations and no more: enumerating, switching and
    // pinning belong to the advanced backend (spec section 55).

    /// <summary>Always empty: the documented API cannot enumerate desktops.</summary>
    public IReadOnlyList<VirtualDesktopInfo> GetDesktops() => Array.Empty<VirtualDesktopInfo>();

    /// <summary>Always null: the documented API cannot tell which desktop is on screen.</summary>
    public VirtualDesktopInfo? GetCurrentDesktop() => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SwitchToDesktop(Guid desktopId) =>
        OperationResult.NotSupported("The documented API cannot switch desktops.");

    /// <remarks>
    /// The shortcut that creates a desktop would work here too, but this backend cannot enumerate
    /// desktops, so it could neither confirm the desktop appeared nor say anything about it.
    /// Reporting nothing is more honest than reporting a desktop it cannot see.
    /// </remarks>
    public VirtualDesktopInfo? CreateDesktop() => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>: without enumerating desktops it could not confirm anything.</summary>
    public OperationResult RemoveDesktop(Guid desktopId) =>
        OperationResult.NotSupported("The documented API cannot remove desktops.");

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SetDesktopName(Guid desktopId, string name) =>
        OperationResult.NotSupported("The documented API cannot rename desktops.");

    /// <summary>Always null: the documented API cannot tell.</summary>
    public bool? IsWindowPinned(nint hwnd) => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SetWindowPinned(nint hwnd, bool pinned) =>
        OperationResult.NotSupported("The documented API cannot pin windows.");

    /// <summary>Always null: the documented API cannot tell.</summary>
    public bool? IsApplicationPinned(nint hwnd) => null;

    /// <summary>Always <see cref="OperationStatus.Unsupported"/>.</summary>
    public OperationResult SetApplicationPinned(nint hwnd, bool pinned) =>
        OperationResult.NotSupported("The documented API cannot pin applications.");

    private IVirtualDesktopManager? GetManager()
    {
        lock (_gate)
        {
            return _disposed ? null : _manager;
        }
    }

    private void LogHResult(string operation, nint hwnd, int hr) =>
        _logger.Debug(
            LogEvents.DesktopOperationFailed,
            ("Operation", operation),
            ("Window", hwnd),
            ("HRESULT", "0x" + hr.ToString("X8")));

    /// <summary>Releases the COM object. Afterwards every call behaves as if the backend were unavailable.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_manager is not null)
            {
                try
                {
                    Marshal.ReleaseComObject(_manager);
                }
                catch
                {
                    // The runtime may already have released it during shutdown.
                }

                _manager = null;
            }
        }
    }
}
