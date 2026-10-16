namespace VirtualDeskManager.Core.Models;

/// <summary>
/// A virtual desktop as the rest of the application sees it: an identifier, its position and
/// the name the user gave it. No COM type ever escapes the backend (spec section 43).
/// </summary>
/// <param name="Id">The identifier Windows gives the desktop. Stable across renames and reordering.</param>
/// <param name="Index">Zero based position, in the same order Task View shows them.</param>
/// <param name="Name">Empty when the user never renamed it; the interface then shows "Desktop N".</param>
public sealed record VirtualDesktopInfo(Guid Id, int Index, string Name)
{
    /// <summary>One based position, the number the user sees and the one hotkeys use.</summary>
    public int Number => Index + 1;

    /// <summary>True when the user gave the desktop a name.</summary>
    public bool HasName => !string.IsNullOrWhiteSpace(Name);

    /// <summary>The name, or <c>Desktop N</c> when it has none. That fallback is not translated.</summary>
    public override string ToString() => HasName ? Name : $"Desktop {Number}";
}
