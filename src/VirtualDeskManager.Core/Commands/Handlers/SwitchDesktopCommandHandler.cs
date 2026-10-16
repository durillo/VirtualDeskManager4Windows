using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Moves to the previous / next desktop through <see cref="IDesktopNavigationService"/>
/// (spec section 15). How the switch actually happens is not visible from here.
/// </summary>
public sealed class SwitchDesktopCommandHandler : ICommandHandler
{
    private readonly IDesktopNavigationService _navigation;

    /// <summary>Creates the handler over the navigation service.</summary>
    public SwitchDesktopCommandHandler(IDesktopNavigationService navigation) => _navigation = navigation;

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[]
    {
        CommandType.SwitchDesktopLeft,
        CommandType.SwitchDesktopRight
    };

    /// <summary>Whatever <see cref="IDesktopNavigationService.IsSupported"/> says.</summary>
    public bool IsSupported(CommandType type) => _navigation.IsSupported;

    /// <inheritdoc/>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_navigation.IsSupported)
        {
            return Task.FromResult(OperationResult.NotSupported("Desktop navigation is disabled in settings."));
        }

        return command.Type == CommandType.SwitchDesktopLeft
            ? _navigation.SwitchLeftAsync(cancellationToken)
            : _navigation.SwitchRightAsync(cancellationToken);
    }
}
