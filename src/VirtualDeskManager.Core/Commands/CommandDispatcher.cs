using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands;

/// <summary>
/// Single entry point for hotkeys, tray, UI and rules (spec sections 7 and 48).
/// A command without a handler is <see cref="OperationStatus.Unsupported"/>, never an exception.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly Dictionary<CommandType, ICommandHandler> _handlers = new();
    private readonly IAppLogger _logger;

    /// <summary>Creates an empty dispatcher. Handlers are added with <see cref="Register"/>.</summary>
    public CommandDispatcher(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <summary>
    /// Routes every type in <see cref="ICommandHandler.SupportedTypes"/> to this handler. A type
    /// that already had one is taken over by the last handler registered.
    /// </summary>
    public void Register(ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        foreach (CommandType type in handler.SupportedTypes)
        {
            _handlers[type] = handler;
        }
    }

    /// <inheritdoc/>
    public bool IsSupported(CommandType type) =>
        _handlers.TryGetValue(type, out ICommandHandler? handler) && handler.IsSupported(type);

    /// <inheritdoc/>
    public async Task<OperationResult> DispatchAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Type == CommandType.None)
        {
            OperationResult invalid = OperationResult.Fail("Unknown or missing command type.");
            _logger.Warn(LogEvents.CommandFailed, ("Command", command.ToString()), ("Status", invalid.Status));
            return invalid;
        }

        if (!_handlers.TryGetValue(command.Type, out ICommandHandler? handler))
        {
            OperationResult unsupported = OperationResult.NotSupported(
                $"'{command.Type}' is not available yet in this build.");

            _logger.Debug(
                LogEvents.CommandFailed,
                ("Command", command.Type),
                ("Source", context.Source),
                ("Status", unsupported.Status));

            return unsupported;
        }

        try
        {
            OperationResult result = await handler
                .ExecuteAsync(command, context, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                // Debug on purpose: the handler already logs the interesting detail at
                // Information level, and two lines per command would be noise (spec section 34).
                _logger.Debug(
                    LogEvents.CommandDispatched,
                    ("Command", command.Type),
                    ("Source", context.Source));
            }
            else
            {
                _logger.Warn(
                    LogEvents.CommandFailed,
                    result.Error ?? "",
                    ("Command", command.Type),
                    ("Source", context.Source),
                    ("Status", result.Status));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("The command was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.CommandFailed, ex, ("Command", command.Type), ("Source", context.Source));
            return OperationResult.Fail(ex.Message);
        }
    }
}
