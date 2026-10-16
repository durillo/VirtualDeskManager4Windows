namespace VirtualDeskManager.Core.Models;

/// <summary>
/// What the currently active virtual desktop backend can actually do (spec section 12 and 14).
/// The UI hides or disables whatever is not available; missing capabilities never throw.
/// </summary>
public sealed class VirtualDesktopCapabilities
{
    /// <summary>Can tell which desktop a window is on.</summary>
    public bool CanGetWindowDesktop { get; init; }

    /// <summary>Can send a window to another desktop.</summary>
    public bool CanMoveWindow { get; init; }

    /// <summary>Can list the desktops in order. Needed for anything relative, such as "the next one".</summary>
    public bool CanEnumerateDesktops { get; init; }

    /// <summary>Can show a given desktop.</summary>
    public bool CanSwitchDesktop { get; init; }

    /// <summary>Can add a desktop.</summary>
    public bool CanCreateDesktop { get; init; }

    /// <summary>Can remove a desktop.</summary>
    public bool CanRemoveDesktop { get; init; }

    /// <summary>Can rename a desktop.</summary>
    public bool CanRenameDesktop { get; init; }

    /// <summary>Can show a single window on every desktop.</summary>
    public bool CanPinWindow { get; init; }

    /// <summary>Can show every window of an application on every desktop.</summary>
    public bool CanPinApplication { get; init; }

    /// <summary>Nothing is available. Used by the null backend and as a safe fallback.</summary>
    public static VirtualDesktopCapabilities None { get; } = new();

    /// <summary>
    /// Each capability with its translation key. Core yields keys, never finished text, so it
    /// stays free of user interface strings.
    /// </summary>
    public IEnumerable<(string Key, bool Available)> Describe()
    {
        yield return ("Capability.GetWindowDesktop", CanGetWindowDesktop);
        yield return ("Capability.MoveWindow", CanMoveWindow);
        yield return ("Capability.EnumerateDesktops", CanEnumerateDesktops);
        yield return ("Capability.SwitchDesktop", CanSwitchDesktop);
        yield return ("Capability.CreateDesktop", CanCreateDesktop);
        yield return ("Capability.RemoveDesktop", CanRemoveDesktop);
        yield return ("Capability.RenameDesktop", CanRenameDesktop);
        yield return ("Capability.PinWindow", CanPinWindow);
        yield return ("Capability.PinApplication", CanPinApplication);
    }
}
