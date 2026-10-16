using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>
/// Left / right desktop navigation (spec section 15). The concrete mechanism is an
/// implementation detail and must not be visible to the rest of the application.
/// </summary>
public interface IDesktopNavigationService
{
    /// <summary>False when no mechanism to change desktops is available; the commands then report it instead of trying.</summary>
    bool IsSupported { get; }

    /// <summary>Goes to the desktop on the left of the current one.</summary>
    Task<OperationResult> SwitchLeftAsync(CancellationToken cancellationToken = default);

    /// <summary>Goes to the desktop on the right of the current one.</summary>
    Task<OperationResult> SwitchRightAsync(CancellationToken cancellationToken = default);
}
