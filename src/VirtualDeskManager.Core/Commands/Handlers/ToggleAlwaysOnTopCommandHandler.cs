using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Toggles Always On Top on the target window without touching its position or size
/// (spec section 10). Accepts an optional <c>enabled</c> parameter to force a state.
/// </summary>
public sealed class ToggleAlwaysOnTopCommandHandler : ICommandHandler
{
    private readonly IWindowManager _windows;
    private readonly IAppLogger _logger;

    /// <summary>Creates the handler over the window manager.</summary>
    public ToggleAlwaysOnTopCommandHandler(IWindowManager windows, IAppLogger? logger = null)
    {
        _windows = windows;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[] { CommandType.ToggleAlwaysOnTop };

    /// <inheritdoc/>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        nint hwnd = context.TargetWindow != 0 ? context.TargetWindow : _windows.GetForegroundWindow();

        if (hwnd == 0)
        {
            return Task.FromResult(OperationResult.InvalidWindow("There is no foreground window."));
        }

        WindowInfo? info = _windows.GetWindowInfo(hwnd);

        if (info is null)
        {
            return Task.FromResult(OperationResult.InvalidWindow());
        }

        bool desired = !info.IsTopMost;

        if (command.TryGetBool("enabled", out bool forced))
        {
            desired = forced;
        }

        OperationResult result = _windows.SetAlwaysOnTop(hwnd, desired);

        _logger.Log(
            result.IsSuccess ? LogLevel.Information : LogLevel.Warning,
            result.IsSuccess ? LogEvents.CommandDispatched : LogEvents.CommandFailed,
            result.Error,
            null,
            ("Command", CommandType.ToggleAlwaysOnTop),
            ("Window", hwnd),
            ("Process", info.ProcessName),
            ("AlwaysOnTop", desired),
            ("Status", result.Status));

        return Task.FromResult(result);
    }
}
