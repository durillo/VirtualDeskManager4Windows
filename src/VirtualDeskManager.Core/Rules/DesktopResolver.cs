using System.Globalization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>
/// Works out which desktop a rule is talking about.
/// </summary>
/// <remarks>
/// rules.json holds a string so a person can read and write it. It is accepted as the name the
/// user gave to the desktop, as its number counting from one, or as its identifier. The name
/// comes first because it is the one that survives reordering, and the number is the one people
/// reach for when their desktops have no names.
/// </remarks>
public static class DesktopResolver
{
    /// <summary>
    /// The desktop <paramref name="value"/> refers to, trying name (ignoring case), then number,
    /// then identifier; null when nothing matches or <paramref name="value"/> is empty.
    /// </summary>
    public static VirtualDesktopInfo? Resolve(string? value, IReadOnlyList<VirtualDesktopInfo> desktops)
    {
        if (string.IsNullOrWhiteSpace(value) || desktops.Count == 0)
        {
            return null;
        }

        string wanted = value.Trim();

        VirtualDesktopInfo? byName = desktops.FirstOrDefault(
            d => d.HasName && string.Equals(d.Name, wanted, StringComparison.CurrentCultureIgnoreCase));

        if (byName is not null)
        {
            return byName;
        }

        if (int.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            && number >= 1
            && number <= desktops.Count)
        {
            return desktops[number - 1];
        }

        if (Guid.TryParse(wanted, out Guid id))
        {
            return desktops.FirstOrDefault(d => d.Id == id);
        }

        return null;
    }

    /// <summary>
    /// How a desktop should be written into a rule: its name when it has one, otherwise its
    /// number. Never the identifier, which means nothing to whoever opens the file.
    /// </summary>
    public static string Describe(VirtualDesktopInfo desktop) =>
        desktop.HasName ? desktop.Name : desktop.Number.ToString(CultureInfo.InvariantCulture);
}
