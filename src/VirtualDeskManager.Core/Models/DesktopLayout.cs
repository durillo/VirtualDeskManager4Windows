namespace VirtualDeskManager.Core.Models;

/// <summary>
/// How many desktops the user had and what each one was called, kept by position.
/// </summary>
/// <remarks>
/// By position and not by identifier on purpose. Windows gives a recreated desktop a brand new
/// GUID, so a layout keyed by identifier would never match anything after the desktops are lost,
/// which is exactly the moment it has to be useful.
/// </remarks>
public sealed class DesktopLayout
{
    /// <summary>Name of each desktop, in the order Task View shows them. Empty means unnamed.</summary>
    public List<string> Names { get; set; } = new();

    /// <summary>How many desktops there were.</summary>
    public int Count => Names.Count;

    /// <summary>True when nothing has been recorded yet, so there is nothing to restore.</summary>
    public bool IsEmpty => Names.Count == 0;

    /// <summary>Records the given desktops, in order. A missing name is stored as empty.</summary>
    public static DesktopLayout From(IEnumerable<VirtualDesktopInfo> desktops) =>
        new() { Names = desktops.Select(d => d.Name ?? string.Empty).ToList() };

    /// <summary>True when both describe the same desktops with the same names.</summary>
    public bool Matches(DesktopLayout other) =>
        other is not null && Names.SequenceEqual(other.Names, StringComparer.Ordinal);

    /// <summary>An independent copy, with its own list of names.</summary>
    public DesktopLayout Clone() => new() { Names = new List<string>(Names) };
}
