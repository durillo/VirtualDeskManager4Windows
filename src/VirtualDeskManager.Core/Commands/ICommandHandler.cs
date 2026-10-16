using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands;

/// <summary>Carries out one family of commands. The dispatcher picks it by command type.</summary>
public interface ICommandHandler
{
    /// <summary>The command types this handler takes care of.</summary>
    IReadOnlyCollection<CommandType> SupportedTypes { get; }

    /// <summary>
    /// False when the handler exists but the active backend cannot perform the command.
    /// The UI uses this to grey out entries instead of failing at runtime (spec section 14).
    /// </summary>
    bool IsSupported(CommandType type) => true;

    /// <summary>
    /// Runs the command. When <see cref="CommandContext.TargetWindow"/> is zero, commands that act
    /// on a window use the foreground one. Problems come back as an <see cref="OperationResult"/>.
    /// </summary>
    Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The single door through which hotkeys, tray, UI and rules run commands (spec sections 7 and 48).
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// Hands the command to its handler and logs the outcome. A command with no handler answers
    /// <see cref="OperationStatus.Unsupported"/>; it never throws for that.
    /// </summary>
    Task<OperationResult> DispatchAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the command has a handler and the active backend can carry it out. The UI uses it
    /// to grey out entries.
    /// </summary>
    bool IsSupported(CommandType type);
}
