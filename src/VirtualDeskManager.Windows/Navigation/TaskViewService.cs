using System.Text;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Navigation;

/// <summary>
/// Opens and closes Task View, the overview of every desktop (spec section 17).
/// </summary>
/// <remarks>
/// Windows has no documented way to do this, so it is driven the way a person would: Win+Tab to
/// open, Escape to close. Measured on build 26200, where Win+Tab brings a window of class
/// <c>XamlExplorerHostIslandWindow</c> to the foreground and Escape hands the foreground back.
///
/// The class is what identifies it, never the title: that reads "Vista de tareas" here and
/// something different on every other Windows language.
/// </remarks>
public sealed class TaskViewService : ITaskViewService
{
    /// <summary>Windows 11 hosts Task View in a XAML island; several windows share this class.</summary>
    private const string TaskViewClass = "XamlExplorerHostIslandWindow";

    private readonly IAppLogger _logger;

    /// <summary>Creates the service. It holds no state: every call looks at the foreground window again.</summary>
    public TaskViewService(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <remarks>
    /// Asks the foreground window and not <c>FindWindow</c>: there are several windows of this
    /// class alive at any time and the others are invisible, so looking one up by class finds a
    /// window that is never on screen and answers no every time.
    /// </remarks>
    public bool IsOpen
    {
        get
        {
            nint foreground = User32.GetForegroundWindow();

            if (foreground == 0)
            {
                return false;
            }

            StringBuilder name = new(64);

            return User32.GetClassName(foreground, name, name.Capacity) > 0
                && string.Equals(name.ToString(), TaskViewClass, StringComparison.Ordinal);
        }
    }

    /// <summary>Opens Task View by sending Win+Tab, unless it is already open.</summary>
    public OperationResult Show()
    {
        // Win+Tab toggles, so sending it while the overview is up would close it. Asking to see
        // the desktops when they are already on screen should leave them there.
        if (IsOpen)
        {
            return OperationResult.Ok();
        }

        OperationResult sent = ShellShortcut.SendWin(NativeConstants.VK_TAB);

        if (sent.IsSuccess)
        {
            _logger.Info(LogEvents.DesktopOperation, ("Operation", nameof(Show)), ("What", "TaskView"));
        }

        return sent;
    }

    /// <summary>Closes Task View by sending Escape.</summary>
    /// <remarks>Answers a conflict, and sends nothing, when Task View is not the foreground window.</remarks>
    public OperationResult Hide()
    {
        // Escape is a plain key: with the overview closed it would reach whatever the user is
        // working in and cancel a dialog, a rename or an autocomplete. It only goes out when
        // Task View is the one holding the foreground.
        if (!IsOpen)
        {
            return OperationResult.Conflicted("Task View is not open.");
        }

        OperationResult sent = ShellShortcut.SendPlain(NativeConstants.VK_ESCAPE);

        if (sent.IsSuccess)
        {
            _logger.Info(LogEvents.DesktopOperation, ("Operation", nameof(Hide)), ("What", "TaskView"));
        }

        return sent;
    }
}
