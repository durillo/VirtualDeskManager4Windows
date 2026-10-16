using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// Brings shortcuts that arrived in a later version to configurations already on disk.
/// </summary>
/// <remarks>
/// The defaults are only written when the file does not exist, so without this nobody who had
/// already run the application would ever get a new shortcut.
/// </remarks>
public static class HotkeyMigrations
{
    /// <summary>
    /// Adds the Ctrl+Alt+1..9 shortcuts that are missing, without touching anything the user has.
    /// </summary>
    /// <remarks>
    /// Two ways to decline, and both are respected: an identifier that already exists is left
    /// exactly as it is, even if the user pointed it somewhere else, and a key combination
    /// already taken by another shortcut is skipped rather than stolen. Registering the same
    /// combination twice would fail at the Win32 call anyway.
    /// </remarks>
    /// <summary>
    /// Combinations the numbered desktop shortcuts have been on and must not stay on.
    /// </summary>
    /// <remarks>
    /// Ctrl+Alt is what AltGr sends, so it ate <c>|</c>, <c>@</c>, <c>#</c> and <c>~</c> system
    /// wide. Ctrl+Win was the first replacement and turned out to be worse: Windows already owns
    /// all nine, so none of them registered at all.
    /// </remarks>
    private static readonly HotkeyModifiers[] Abandoned =
    {
        HotkeyModifiers.Control | HotkeyModifiers.Alt,
        HotkeyModifiers.Control | HotkeyModifiers.Windows
    };

    /// <summary>
    /// Moves the numbered desktop shortcuts off any combination they should never have been on.
    /// </summary>
    /// <remarks>
    /// Worth undoing on everyone's machine and not only in the defaults, because the first one of
    /// these took four characters away from every Spanish keyboard and the second one simply did
    /// not work.
    ///
    /// Only shortcuts still exactly as they shipped are touched. If the combination or the target
    /// was changed, it is left alone: the user chose it, even if what they chose is Ctrl+Alt.
    /// </remarks>
    /// <returns>The shortcuts that were moved, empty when there was nothing to do.</returns>
    public static IReadOnlyList<HotkeyDefinition> RepairNumberedDesktopHotkeys(List<HotkeyDefinition> hotkeys)
    {
        HashSet<(HotkeyModifiers, HotkeyKey)> taken = new(hotkeys.Select(h => (h.Modifiers, h.Key)));
        List<HotkeyDefinition> moved = new();

        for (int number = 1; number <= 9; number++)
        {
            HotkeyKey key = (HotkeyKey)((int)HotkeyKey.D1 + number - 1);

            HotkeyDefinition? shortcut = hotkeys.FirstOrDefault(h =>
                string.Equals(h.Id, DefaultConfiguration.NumberedDesktopId(number), StringComparison.OrdinalIgnoreCase)
                && Abandoned.Contains(h.Modifiers)
                && h.Key == key
                && h.Command.Type == CommandType.SwitchToDesktop
                && h.Command.TryGetInt("number", out int target)
                && target == number);

            if (shortcut is null)
            {
                continue;
            }

            // Landing on top of something else would trade one broken shortcut for another.
            if (taken.Contains((DefaultConfiguration.NumberedDesktopModifiers, key)))
            {
                continue;
            }

            taken.Remove((shortcut.Modifiers, shortcut.Key));
            shortcut.Modifiers = DefaultConfiguration.NumberedDesktopModifiers;
            taken.Add((shortcut.Modifiers, shortcut.Key));

            moved.Add(shortcut);
        }

        return moved;
    }

    /// <summary>
    /// Moves the drop down console off the Ñ and onto the key it was always meant to be on.
    /// </summary>
    /// <remarks>
    /// The console shipped in 0.7.0 bound to <c>OemTilde</c>, described everywhere as "the key
    /// under Escape" because that is what the name means on a keyboard from the United States.
    /// On a Spanish keyboard that code is the Ñ; the key under Escape, the one with º ª \ on it,
    /// is <c>Oem5</c>. So the shortcut worked, but not on the key the documentation promised, and
    /// it quietly took the Ñ away from every program on the machine.
    ///
    /// Only a shortcut still exactly as it shipped is moved. Anyone who chose the Ñ on purpose
    /// keeps it.
    /// </remarks>
    /// <returns>The shortcut that was moved, or empty when there was nothing to do.</returns>
    public static IReadOnlyList<HotkeyDefinition> RepairDropDownConsoleHotkey(List<HotkeyDefinition> hotkeys)
    {
        const HotkeyModifiers shipped = HotkeyModifiers.Control | HotkeyModifiers.Shift;

        HotkeyDefinition? shortcut = hotkeys.FirstOrDefault(h =>
            string.Equals(h.Id, DefaultConfiguration.ToggleDropDownConsoleId, StringComparison.OrdinalIgnoreCase)
            && h.Key == HotkeyKey.OemTilde
            && (h.Modifiers & ~HotkeyModifiers.NoRepeat) == shipped
            && h.Command.Type == CommandType.ToggleDropDownConsole);

        if (shortcut is null)
        {
            return Array.Empty<HotkeyDefinition>();
        }

        // Landing on top of something else would trade one wrong key for a collision.
        if (hotkeys.Any(h => !ReferenceEquals(h, shortcut)
                             && h.Key == HotkeyKey.Oem5
                             && (h.Modifiers & ~HotkeyModifiers.NoRepeat) == shipped))
        {
            return Array.Empty<HotkeyDefinition>();
        }

        shortcut.Key = HotkeyKey.Oem5;

        return new[] { shortcut };
    }

    /// <returns>The shortcuts that were added, empty when there was nothing to do.</returns>
    public static IReadOnlyList<HotkeyDefinition> AddNumberedDesktopHotkeys(List<HotkeyDefinition> hotkeys) =>
        Add(hotkeys, DefaultConfiguration.CreateNumberedDesktopHotkeys());

    /// <summary>Adds the show / hide all desktops shortcuts, which arrived later as well.</summary>
    public static IReadOnlyList<HotkeyDefinition> AddTaskViewHotkeys(List<HotkeyDefinition> hotkeys) =>
        Add(hotkeys, DefaultConfiguration.CreateTaskViewHotkeys());

    /// <summary>Adds the shortcut of the overview this application draws itself, later still.</summary>
    public static IReadOnlyList<HotkeyDefinition> AddDesktopGridHotkeys(List<HotkeyDefinition> hotkeys) =>
        Add(hotkeys, DefaultConfiguration.CreateDesktopGridHotkeys());

    /// <summary>And the one that rolls the console down from the top of the screen.</summary>
    public static IReadOnlyList<HotkeyDefinition> AddDropDownConsoleHotkeys(List<HotkeyDefinition> hotkeys) =>
        Add(hotkeys, DefaultConfiguration.CreateDropDownConsoleHotkeys());

    /// <summary>Y el que la fija para que no se recoja sola.</summary>
    public static IReadOnlyList<HotkeyDefinition> AddDropDownConsolePinHotkeys(List<HotkeyDefinition> hotkeys) =>
        Add(hotkeys, DefaultConfiguration.CreateDropDownConsolePinHotkeys());

    private static IReadOnlyList<HotkeyDefinition> Add(
        List<HotkeyDefinition> hotkeys,
        IEnumerable<HotkeyDefinition> candidates)
    {
        HashSet<string> ids = new(hotkeys.Select(h => h.Id), StringComparer.OrdinalIgnoreCase);
        HashSet<(HotkeyModifiers, HotkeyKey)> taken = new(hotkeys.Select(h => (h.Modifiers, h.Key)));

        List<HotkeyDefinition> added = new();

        foreach (HotkeyDefinition candidate in candidates)
        {
            if (ids.Contains(candidate.Id) || taken.Contains((candidate.Modifiers, candidate.Key)))
            {
                continue;
            }

            hotkeys.Add(candidate);
            added.Add(candidate);

            // Se apunta lo que se acaba de coger: dos candidatos con la misma combinación
            // entrarían los dos, y el segundo fallaría al registrarse en Windows.
            ids.Add(candidate.Id);
            taken.Add((candidate.Modifiers, candidate.Key));
        }

        return added;
    }
}
