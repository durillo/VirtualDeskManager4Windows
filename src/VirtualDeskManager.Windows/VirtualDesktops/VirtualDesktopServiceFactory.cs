using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Windows.VirtualDesktops.Internal;
using VirtualDeskManager.Windows.VirtualDesktops.Public;

namespace VirtualDeskManager.Windows.VirtualDesktops;

/// <summary>
/// Chooses the virtual desktop backend at start up (spec section 44).
/// </summary>
/// <remarks>
/// The advanced backend is tried first because it is the only one that can move windows of other
/// applications between desktops and show a window on all of them. If this Windows build does not
/// speak its interfaces, or if what it answers does not match what the registry says, the
/// application falls back to the documented backend with fewer capabilities: degraded, never
/// broken, and never stopped.
/// </remarks>
public static class VirtualDesktopServiceFactory
{
    /// <summary>
    /// Returns the most capable backend this build supports: the advanced one, else the documented
    /// one, else <see cref="NullVirtualDesktopService"/>. Never null.
    /// </summary>
    public static IVirtualDesktopService Create(IAppLogger? logger = null)
    {
        IAppLogger log = logger ?? NullAppLogger.Instance;

        PublicVirtualDesktopService? documented = PublicVirtualDesktopService.TryCreate(log);
        InternalVirtualDesktopService? advanced = InternalVirtualDesktopService.TryCreate(documented, log);

        if (advanced is not null)
        {
            log.Info(
                LogEvents.DesktopBackendSelected,
                ("Backend", advanced.BackendName),
                ("WindowsVersion", Environment.OSVersion.Version.ToString()));

            return advanced;
        }

        if (documented is not null)
        {
            log.Info(
                LogEvents.DesktopBackendSelected,
                ("Backend", documented.BackendName),
                ("WindowsVersion", Environment.OSVersion.Version.ToString()),
                ("Reason", "the advanced backend is not available on this build"));

            return documented;
        }

        log.Warn(
            LogEvents.DesktopBackendUnavailable,
            "No virtual desktop backend could be initialised; the related features stay disabled.");

        return new NullVirtualDesktopService();
    }
}
