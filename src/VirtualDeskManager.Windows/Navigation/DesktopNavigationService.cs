using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Windows.Navigation;

/// <summary>
/// Left / right desktop navigation (spec section 15), preferring the real thing.
/// </summary>
/// <remarks>
/// When the active backend can switch desktops the jump is exact: the desktop list is read, the
/// neighbour is picked and the shell is told to go there. Only when that is unavailable does it
/// fall back to synthesizing the shell shortcut, which is the compatibility path.
/// </remarks>
public sealed class DesktopNavigationService : IDesktopNavigationService
{
    private readonly IVirtualDesktopService _desktops;
    private readonly IDesktopNavigationService _fallback;

    /// <summary>Navigation that switches through <paramref name="desktops"/> when it can.</summary>
    /// <param name="desktops">Active virtual desktop backend, used for the exact jump.</param>
    /// <param name="fallback">Used when the backend cannot list or switch desktops.</param>
    public DesktopNavigationService(IVirtualDesktopService desktops, IDesktopNavigationService fallback)
    {
        _desktops = desktops;
        _fallback = fallback;
    }

    /// <summary>True when either the backend or the fallback can change desktops.</summary>
    public bool IsSupported => CanSwitchDirectly || _fallback.IsSupported;

    private bool CanSwitchDirectly =>
        _desktops.Capabilities.CanSwitchDesktop && _desktops.Capabilities.CanEnumerateDesktops;

    /// <summary>Goes to the desktop on the left. On the first one it answers a conflict: there is no wrapping.</summary>
    public Task<OperationResult> SwitchLeftAsync(CancellationToken cancellationToken = default) =>
        CanSwitchDirectly
            ? Task.FromResult(Step(-1))
            : _fallback.SwitchLeftAsync(cancellationToken);

    /// <summary>Goes to the desktop on the right. On the last one it answers a conflict: there is no wrapping.</summary>
    public Task<OperationResult> SwitchRightAsync(CancellationToken cancellationToken = default) =>
        CanSwitchDirectly
            ? Task.FromResult(Step(+1))
            : _fallback.SwitchRightAsync(cancellationToken);

    private OperationResult Step(int direction)
    {
        IReadOnlyList<VirtualDesktopInfo> all = _desktops.GetDesktops();
        VirtualDesktopInfo? current = _desktops.GetCurrentDesktop();

        if (current is null || all.Count == 0)
        {
            return OperationResult.Fail("The current desktop could not be determined.");
        }

        int index = current.Index + direction;

        // Windows does not wrap around at the ends, and neither does this.
        if (index < 0 || index >= all.Count)
        {
            return OperationResult.Conflicted("There is no desktop on that side.");
        }

        return _desktops.SwitchToDesktop(all[index].Id);
    }
}
