using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Shows and hides the overview this application draws itself, through
/// <see cref="IDesktopGridView"/>. That it is a WPF window is not visible from here.
/// </summary>
public sealed class DesktopGridCommandHandler : ICommandHandler
{
    private readonly IDesktopGridView _grid;

    /// <summary>Creates the handler over the overview window.</summary>
    public DesktopGridCommandHandler(IDesktopGridView grid) => _grid = grid;

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[]
    {
        CommandType.ShowDesktopGrid,
        CommandType.HideDesktopGrid,
        CommandType.ToggleDesktopGrid
    };

    /// <summary>Always true: the overview is drawn by this application and does not depend on the backend.</summary>
    public bool IsSupported(CommandType type) => true;

    /// <inheritdoc/>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        OperationResult result = command.Type switch
        {
            CommandType.ShowDesktopGrid => _grid.Show(),

            CommandType.HideDesktopGrid => _grid.Hide(),

            // One key that opens and closes it is what a person expects from an overview, and it
            // saves spending a second combination on the way out.
            _ => _grid.IsOpen ? _grid.Hide() : _grid.Show()
        };

        return Task.FromResult(result);
    }
}
