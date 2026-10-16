using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Shows and hides the overview of every desktop (spec section 17), through
/// <see cref="ITaskViewService"/>. How it is actually opened is not visible from here.
/// </summary>
public sealed class TaskViewCommandHandler : ICommandHandler
{
    private readonly ITaskViewService _taskView;

    /// <summary>Creates the handler over the Task View service.</summary>
    public TaskViewCommandHandler(ITaskViewService taskView) => _taskView = taskView;

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[]
    {
        CommandType.ShowAllDesktops,
        CommandType.HideAllDesktops
    };

    /// <summary>Always true: failures are reported by the result of each call.</summary>
    public bool IsSupported(CommandType type) => true;

    /// <inheritdoc/>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(command.Type == CommandType.ShowAllDesktops ? _taskView.Show() : _taskView.Hide());
}
