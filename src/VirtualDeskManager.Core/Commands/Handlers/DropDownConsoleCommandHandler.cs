using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Rolls the drop down console in and out, through <see cref="IDropDownConsole"/>.
/// </summary>
public sealed class DropDownConsoleCommandHandler : ICommandHandler
{
    private readonly IDropDownConsole _console;

    /// <summary>Creates the handler over the console.</summary>
    public DropDownConsoleCommandHandler(IDropDownConsole console) => _console = console;

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[]
    {
        CommandType.ToggleDropDownConsole,
        CommandType.ToggleDropDownConsolePin
    };

    /// <summary>Always true: the console does not depend on the virtual desktop backend.</summary>
    public bool IsSupported(CommandType type) => true;

    /// <summary>Despliega o recoge la consola, o la fija o la suelta.</summary>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(command.Type == CommandType.ToggleDropDownConsolePin
            ? _console.TogglePin()
            : _console.Toggle());
}
