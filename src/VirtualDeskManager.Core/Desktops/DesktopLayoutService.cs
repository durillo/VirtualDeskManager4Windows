using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Desktops;

/// <summary>
/// Remembers how many desktops the user had and what they were called, and puts them back when
/// Windows loses them (spec section 15).
/// </summary>
/// <remarks>
/// Windows does drop the whole set from time to time, and when it does, every rule that names a
/// desktop by number starts failing at once. The layout is saved whenever it is seen to change,
/// so what gets restored is the last thing the user actually had, not a snapshot from install
/// time.
/// </remarks>
public sealed class DesktopLayoutService
{
    private readonly IVirtualDesktopService _desktops;
    private readonly IConfigurationService _configuration;
    private readonly IAppLogger _logger;

    /// <summary>Creates the service over the desktop backend and the stored configuration.</summary>
    public DesktopLayoutService(
        IVirtualDesktopService desktops,
        IConfigurationService configuration,
        IAppLogger? logger = null)
    {
        _desktops = desktops;
        _configuration = configuration;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>What happened during a restore, for the log and for the tests.</summary>
    public sealed record RestoreOutcome(int Created, int Renamed, bool Skipped, string? Reason = null);

    /// <summary>
    /// Saves the current desktops when they differ from what was stored. Cheap enough to call
    /// from anywhere that already had to enumerate them, which is what keeps the stored layout
    /// current without polling.
    /// </summary>
    public bool Remember()
    {
        if (!_desktops.Capabilities.CanEnumerateDesktops)
        {
            return false;
        }

        IReadOnlyList<VirtualDesktopInfo> current = _desktops.GetDesktops();

        // An empty answer means the backend could not enumerate, not that the user has no
        // desktops: there is always at least one. Saving it would erase the layout.
        if (current.Count == 0)
        {
            return false;
        }

        DesktopLayout seen = DesktopLayout.From(current);

        if (seen.Matches(_configuration.Settings.Desktops))
        {
            return false;
        }

        _configuration.Settings.Desktops = seen;
        _configuration.SaveSettings();

        _logger.Debug(LogEvents.DesktopOperation, ("Operation", "RememberLayout"), ("Desktops", seen.Count));
        return true;
    }

    /// <summary>
    /// Creates the desktops missing to reach the saved count and puts every name back.
    /// </summary>
    /// <remarks>
    /// Windows moves the view to each desktop it creates, so where the user was is captured first
    /// and restored at the end. Without that, starting the application would dump them on the last
    /// desktop for no reason they could see.
    /// </remarks>
    public RestoreOutcome Restore()
    {
        AppSettings settings = _configuration.Settings;

        if (!settings.RestoreDesktopLayout)
        {
            return new RestoreOutcome(0, 0, Skipped: true, "disabled in settings");
        }

        if (settings.Desktops.IsEmpty)
        {
            return new RestoreOutcome(0, 0, Skipped: true, "nothing saved yet");
        }

        if (!_desktops.Capabilities.CanEnumerateDesktops)
        {
            return new RestoreOutcome(0, 0, Skipped: true, "the backend cannot enumerate desktops");
        }

        IReadOnlyList<VirtualDesktopInfo> current = _desktops.GetDesktops();

        if (current.Count == 0)
        {
            return new RestoreOutcome(0, 0, Skipped: true, "the desktops could not be read");
        }

        int missing = settings.Desktops.Count - current.Count;
        int created = 0;

        if (missing > 0 && !_desktops.Capabilities.CanCreateDesktop)
        {
            return new RestoreOutcome(0, 0, Skipped: true, "the backend cannot create desktops");
        }

        Guid origin = _desktops.GetCurrentDesktop()?.Id ?? Guid.Empty;

        for (int i = 0; i < missing; i++)
        {
            if (_desktops.CreateDesktop() is null)
            {
                _logger.Warn(
                    LogEvents.DesktopOperationFailed,
                    ("Operation", "RestoreLayout"),
                    ("Reason", "a desktop could not be created; the rest is skipped"));
                break;
            }

            created++;
        }

        int renamed = Rename(settings.Desktops);

        if (origin != Guid.Empty && _desktops.GetCurrentDesktop()?.Id != origin)
        {
            _desktops.SwitchToDesktop(origin);
        }

        if (created > 0 || renamed > 0)
        {
            _logger.Info(
                LogEvents.DesktopOperation,
                ("Operation", "RestoreLayout"),
                ("Created", created),
                ("Renamed", renamed));
        }

        // Store what actually ended up existing, which is not the target when a creation failed.
        Remember();

        return new RestoreOutcome(created, renamed, Skipped: false);
    }

    /// <summary>Applies the saved names by position, leaving any extra desktop alone.</summary>
    private int Rename(DesktopLayout saved)
    {
        if (!_desktops.Capabilities.CanRenameDesktop)
        {
            return 0;
        }

        IReadOnlyList<VirtualDesktopInfo> desktops = _desktops.GetDesktops();
        int renamed = 0;

        for (int i = 0; i < desktops.Count && i < saved.Names.Count; i++)
        {
            string wanted = saved.Names[i];

            // An empty saved name says "it never had one", which is not a reason to wipe a name
            // the user has given it since.
            if (string.IsNullOrEmpty(wanted) || string.Equals(desktops[i].Name, wanted, StringComparison.Ordinal))
            {
                continue;
            }

            if (_desktops.SetDesktopName(desktops[i].Id, wanted).IsSuccess)
            {
                renamed++;
            }
        }

        return renamed;
    }
}
