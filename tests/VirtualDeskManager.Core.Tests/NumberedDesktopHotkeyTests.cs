using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>The numbered desktop shortcuts, and how they reach a configuration already on disk.</summary>
public class NumberedDesktopHotkeyTests
{
    // AltGr IS Ctrl+Alt. Shipping these on Ctrl+Alt took |, @, # and ~ away from every Spanish
    // keyboard on the machine, so no default may ever go back there.
    [Fact]
    public void They_never_use_the_combination_AltGr_sends()
    {
        HotkeyModifiers altGr = HotkeyModifiers.Control | HotkeyModifiers.Alt;

        Assert.All(DefaultConfiguration.CreateNumberedDesktopHotkeys(), h => Assert.NotEqual(altGr, h.Modifiers));
    }

    [Fact]
    public void A_configuration_that_still_has_them_on_AltGr_is_moved_off_it()
    {
        List<HotkeyDefinition> hotkeys = ConLosAntiguos();

        IReadOnlyList<HotkeyDefinition> moved = HotkeyMigrations.RepairNumberedDesktopHotkeys(hotkeys);

        Assert.Equal(9, moved.Count);
        Assert.All(hotkeys, h => Assert.NotEqual(HotkeyModifiers.Control | HotkeyModifiers.Alt, h.Modifiers));
    }

    [Fact]
    public void Moving_them_twice_changes_nothing_the_second_time()
    {
        List<HotkeyDefinition> hotkeys = ConLosAntiguos();

        HotkeyMigrations.RepairNumberedDesktopHotkeys(hotkeys);

        Assert.Empty(HotkeyMigrations.RepairNumberedDesktopHotkeys(hotkeys));
    }

    // If the user moved one somewhere else, that is their choice, even if it is Ctrl+Alt.
    [Fact]
    public void A_shortcut_the_user_changed_is_left_where_they_put_it()
    {
        List<HotkeyDefinition> hotkeys = ConLosAntiguos();
        hotkeys[0].Key = HotkeyKey.F5;

        IReadOnlyList<HotkeyDefinition> moved = HotkeyMigrations.RepairNumberedDesktopHotkeys(hotkeys);

        Assert.Equal(8, moved.Count);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hotkeys[0].Modifiers);
    }

    /// <summary>The nine shortcuts exactly as the first version wrote them: on Ctrl+Alt.</summary>
    private static List<HotkeyDefinition> ConLosAntiguos()
    {
        List<HotkeyDefinition> hotkeys = DefaultConfiguration.CreateNumberedDesktopHotkeys().ToList();

        foreach (HotkeyDefinition hotkey in hotkeys)
        {
            hotkey.Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        }

        return hotkeys;
    }

    [Fact]
    public void There_is_one_shortcut_per_desktop_from_one_to_nine()
    {
        List<HotkeyDefinition> numbered = DefaultConfiguration.CreateNumberedDesktopHotkeys().ToList();

        Assert.Equal(9, numbered.Count);
        Assert.All(numbered, h => Assert.Equal(CommandType.SwitchToDesktop, h.Command.Type));
        Assert.All(numbered, h => Assert.True(h.IsValid));
    }

    [Fact]
    public void Each_one_points_at_its_own_desktop()
    {
        List<HotkeyDefinition> numbered = DefaultConfiguration.CreateNumberedDesktopHotkeys().ToList();

        for (int i = 0; i < numbered.Count; i++)
        {
            Assert.True(numbered[i].Command.TryGetInt("number", out int number));
            Assert.Equal(i + 1, number);
            Assert.Equal((HotkeyKey)((int)HotkeyKey.D1 + i), numbered[i].Key);
        }
    }

    [Fact]
    public void They_are_in_the_defaults_of_a_new_installation()
    {
        List<HotkeyDefinition> defaults = DefaultConfiguration.CreateHotkeys();

        Assert.Equal(9, defaults.Count(h => h.Command.Type == CommandType.SwitchToDesktop));
    }

    // Anyone who had already run the application has a hotkeys.json, and the defaults are only
    // written when there is no file, so without the migration they would never see these.
    [Fact]
    public void They_are_added_to_a_configuration_that_predates_them()
    {
        List<HotkeyDefinition> existing = DefaultConfiguration.CreateHotkeys()
            .Where(h => h.Command.Type != CommandType.SwitchToDesktop)
            .ToList();

        IReadOnlyList<HotkeyDefinition> added = HotkeyMigrations.AddNumberedDesktopHotkeys(existing);

        Assert.Equal(9, added.Count);
        Assert.Equal(9, existing.Count(h => h.Command.Type == CommandType.SwitchToDesktop));
    }

    [Fact]
    public void Running_the_migration_twice_does_not_duplicate_them()
    {
        List<HotkeyDefinition> hotkeys = DefaultConfiguration.CreateHotkeys();

        Assert.Empty(HotkeyMigrations.AddNumberedDesktopHotkeys(hotkeys));
        Assert.Equal(9, hotkeys.Count(h => h.Command.Type == CommandType.SwitchToDesktop));
    }

    // Taking a combination the user had already given to something else would break their setup,
    // and registering the same one twice fails at the Win32 call anyway.
    [Fact]
    public void A_combination_the_user_already_uses_is_left_alone()
    {
        List<HotkeyDefinition> hotkeys = new()
        {
            new HotkeyDefinition
            {
                Id = "mine",
                Modifiers = DefaultConfiguration.NumberedDesktopModifiers,
                Key = HotkeyKey.D1,
                Command = new CommandDefinition(CommandType.ToggleAlwaysOnTop)
            }
        };

        IReadOnlyList<HotkeyDefinition> added = HotkeyMigrations.AddNumberedDesktopHotkeys(hotkeys);

        Assert.Equal(8, added.Count);
        Assert.Equal(CommandType.ToggleAlwaysOnTop, hotkeys.Single(h => h.Key == HotkeyKey.D1).Command.Type);
    }
}
