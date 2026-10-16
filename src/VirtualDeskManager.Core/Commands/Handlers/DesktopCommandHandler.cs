using System.Globalization;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Everything that acts on virtual desktops: moving a window to a desktop, to the previous or the
/// next one, jumping to a desktop and showing a window on all of them (spec sections 16 to 18).
/// </summary>
/// <remarks>
/// The handler never touches COM: it goes through <see cref="IVirtualDesktopService"/> and asks
/// <see cref="VirtualDesktopCapabilities"/> before offering anything, so a backend that cannot do
/// something answers Unsupported instead of failing (spec section 14).
/// </remarks>
public sealed class DesktopCommandHandler : ICommandHandler
{
    private readonly IWindowManager _windows;
    private readonly IVirtualDesktopService _desktops;
    private readonly Func<bool> _followMovedWindow;
    private readonly IAppLogger _logger;

    /// <summary>Creates the handler over the window manager and the desktop backend.</summary>
    /// <param name="windows">Finds and checks the window to act on.</param>
    /// <param name="desktops">The virtual desktop backend.</param>
    /// <param name="followMovedWindow">
    /// Asked after every move: true makes the view switch to the desktop the window went to. It is
    /// a function so that a change in the settings applies without rebuilding the handler.
    /// </param>
    /// <param name="logger">Log; none if null.</param>
    public DesktopCommandHandler(
        IWindowManager windows,
        IVirtualDesktopService desktops,
        Func<bool>? followMovedWindow = null,
        IAppLogger? logger = null)
    {
        _windows = windows;
        _desktops = desktops;
        _followMovedWindow = followMovedWindow ?? (() => false);
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[]
    {
        CommandType.MoveWindowToDesktop,
        CommandType.MoveWindowLeft,
        CommandType.MoveWindowRight,
        CommandType.SwitchToDesktop,
        CommandType.TogglePinWindow
    };

    /// <summary>
    /// Asks the backend's <see cref="VirtualDesktopCapabilities"/>. Moving left or right also needs
    /// the desktops to be enumerable, to know which one is next.
    /// </summary>
    public bool IsSupported(CommandType type) => type switch
    {
        CommandType.TogglePinWindow => _desktops.Capabilities.CanPinWindow,
        CommandType.SwitchToDesktop => _desktops.Capabilities.CanSwitchDesktop,
        CommandType.MoveWindowToDesktop => _desktops.Capabilities.CanMoveWindow,
        _ => _desktops.Capabilities.CanMoveWindow && _desktops.Capabilities.CanEnumerateDesktops
    };

    /// <inheritdoc/>
    /// <remarks>
    /// A pinned window is unpinned before being sent to a single desktop: it is an explicit order
    /// from the user, so it wins over the pin.
    /// </remarks>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported(command.Type))
        {
            return Task.FromResult(OperationResult.NotSupported(
                $"The '{_desktops.BackendName}' backend cannot do this."));
        }

        nint hwnd = context.TargetWindow != 0 ? context.TargetWindow : _windows.GetForegroundWindow();

        return Task.FromResult(command.Type switch
        {
            CommandType.TogglePinWindow => TogglePin(hwnd),
            CommandType.SwitchToDesktop => Switch(command),
            _ => Move(command, hwnd)
        });
    }

    private OperationResult TogglePin(nint hwnd)
    {
        if (hwnd == 0 || !_windows.IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        bool? pinned = _desktops.IsWindowPinned(hwnd);

        if (pinned is null)
        {
            return OperationResult.NotSupported("Windows will not say whether that window is pinned.");
        }

        return _desktops.SetWindowPinned(hwnd, !pinned.Value);
    }

    private OperationResult Switch(CommandDefinition command)
    {
        VirtualDesktopInfo? target = Resolve(command);

        return target is null
            ? OperationResult.Fail("The target desktop could not be determined.")
            : _desktops.SwitchToDesktop(target.Id);
    }

    private OperationResult Move(CommandDefinition command, nint hwnd)
    {
        if (hwnd == 0 || !_windows.IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        Guid? windowDesktop = _desktops.GetWindowDesktopId(hwnd);
        IReadOnlyList<VirtualDesktopInfo> all = _desktops.GetDesktops();

        VirtualDesktopInfo? origin = windowDesktop is Guid id
            ? all.FirstOrDefault(d => d.Id == id)
            : _desktops.GetCurrentDesktop();

        VirtualDesktopInfo? target = command.Type switch
        {
            CommandType.MoveWindowLeft => Neighbour(all, origin, -1),
            CommandType.MoveWindowRight => Neighbour(all, origin, +1),
            _ => Resolve(command)
        };

        if (target is null)
        {
            return command.Type is CommandType.MoveWindowLeft or CommandType.MoveWindowRight
                ? OperationResult.Conflicted("There is no desktop on that side.")
                : OperationResult.Fail("The target desktop could not be determined.");
        }

        if (origin is not null && target.Id == origin.Id)
        {
            return OperationResult.Ok();
        }

        // Sending a window to one desktop is the opposite of showing it on all of them, so the
        // pin has to go. Windows drops it by itself when the view moves, but doing it here makes
        // the outcome deliberate and logged instead of an undocumented side effect. Unlike a
        // rule, this is an explicit order from the user, so it wins over the pin.
        if (_desktops.IsWindowPinned(hwnd) == true)
        {
            _logger.Info(
                LogEvents.DesktopOperation,
                ("Operation", "UnpinBeforeMove"),
                ("Window", hwnd));

            _desktops.SetWindowPinned(hwnd, false);
        }

        OperationResult result = _desktops.MoveWindowToDesktop(hwnd, target.Id);

        if (!result.IsSuccess)
        {
            return result;
        }

        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", "MoveWindow"),
            ("Window", hwnd),
            ("Desktop", target.Number));

        // "Follow the window" is a setting, not a fixed behaviour (spec section 16).
        if (_followMovedWindow() && _desktops.Capabilities.CanSwitchDesktop)
        {
            OperationResult jump = _desktops.SwitchToDesktop(target.Id);

            if (!jump.IsSuccess)
            {
                _logger.Warn(
                    LogEvents.DesktopOperationFailed,
                    jump.Error ?? "",
                    ("Operation", "FollowMovedWindow"));
            }
        }

        return OperationResult.Ok();
    }

    private static VirtualDesktopInfo? Neighbour(
        IReadOnlyList<VirtualDesktopInfo> all,
        VirtualDesktopInfo? origin,
        int direction)
    {
        if (origin is null)
        {
            return null;
        }

        int index = origin.Index + direction;

        // Windows does not wrap around at the ends, and neither does this.
        return index >= 0 && index < all.Count ? all[index] : null;
    }

    /// <summary>
    /// Reads the target from the command: a desktop identifier, a one based number, a zero based
    /// index or a name. Returns null when the command says nothing, which is reported as a
    /// failure rather than silently doing nothing.
    /// </summary>
    private VirtualDesktopInfo? Resolve(CommandDefinition command)
    {
        IReadOnlyList<VirtualDesktopInfo> all = _desktops.GetDesktops();

        if (command.TryGetGuid("desktop", out Guid id))
        {
            return all.FirstOrDefault(d => d.Id == id);
        }

        if (command.TryGetInt("number", out int number) && number >= 1 && number <= all.Count)
        {
            return all[number - 1];
        }

        if (command.TryGetInt("index", out int index) && index >= 0 && index < all.Count)
        {
            return all[index];
        }

        string? name = command.GetString("name");

        if (!string.IsNullOrWhiteSpace(name))
        {
            return all.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        _logger.Debug(
            LogEvents.CommandFailed,
            ("Command", command.Type),
            ("Reason", "no desktop parameter"),
            ("Parameters", command.Parameters.Count.ToString(CultureInfo.InvariantCulture)));

        return null;
    }
}
