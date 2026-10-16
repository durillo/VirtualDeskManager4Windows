using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// Moving the drop down console off the key it was wrongly bound to in 0.7.0.
/// </summary>
/// <remarks>
/// It shipped on <c>OemTilde</c>, called "the key under Escape" because that is what the name
/// means on a keyboard from the United States. On a Spanish keyboard that code is the Ñ, and the
/// key under Escape is <c>Oem5</c>. The shortcut worked, but not where the documentation said,
/// and it took the Ñ away from every program on the machine.
/// </remarks>
public class DropDownConsoleHotkeyRepairTests
{
    private const HotkeyModifiers Shipped = HotkeyModifiers.Control | HotkeyModifiers.Shift;

    private static HotkeyDefinition Shortcut(
        HotkeyKey key = HotkeyKey.OemTilde,
        HotkeyModifiers modifiers = Shipped,
        string? id = null) =>
        new()
        {
            Id = id ?? DefaultConfiguration.ToggleDropDownConsoleId,
            Key = key,
            Modifiers = modifiers,
            Command = new CommandDefinition(CommandType.ToggleDropDownConsole)
        };

    [Fact]
    public void The_shipped_shortcut_is_moved_to_the_key_under_escape()
    {
        List<HotkeyDefinition> hotkeys = new() { Shortcut() };

        IReadOnlyList<HotkeyDefinition> moved = HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys);

        Assert.Single(moved);
        Assert.Equal(HotkeyKey.Oem5, hotkeys[0].Key);
        Assert.Equal(Shipped, hotkeys[0].Modifiers);
    }

    [Fact]
    public void Running_it_twice_changes_nothing_the_second_time()
    {
        List<HotkeyDefinition> hotkeys = new() { Shortcut() };

        HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys);

        Assert.Empty(HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys));
        Assert.Equal(HotkeyKey.Oem5, hotkeys[0].Key);
    }

    /// <summary>Someone who chose the Ñ on purpose keeps it.</summary>
    [Fact]
    public void A_shortcut_the_user_changed_is_left_alone()
    {
        List<HotkeyDefinition> hotkeys = new()
        {
            Shortcut(modifiers: HotkeyModifiers.Control | HotkeyModifiers.Alt)
        };

        Assert.Empty(HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys));
        Assert.Equal(HotkeyKey.OemTilde, hotkeys[0].Key);
    }

    [Fact]
    public void A_shortcut_already_on_another_key_is_left_alone()
    {
        List<HotkeyDefinition> hotkeys = new() { Shortcut(key: HotkeyKey.F9) };

        Assert.Empty(HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys));
        Assert.Equal(HotkeyKey.F9, hotkeys[0].Key);
    }

    /// <summary>Moving it on top of something else would trade one wrong key for a collision.</summary>
    [Fact]
    public void It_does_not_move_onto_a_combination_already_taken()
    {
        List<HotkeyDefinition> hotkeys = new()
        {
            Shortcut(),
            new HotkeyDefinition
            {
                Id = "algo-mio",
                Key = HotkeyKey.Oem5,
                Modifiers = Shipped,
                Command = new CommandDefinition(CommandType.ToggleAlwaysOnTop)
            }
        };

        Assert.Empty(HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys));
        Assert.Equal(HotkeyKey.OemTilde, hotkeys[0].Key);
    }

    [Fact]
    public void A_configuration_without_the_console_shortcut_survives()
    {
        List<HotkeyDefinition> hotkeys = new();

        Assert.Empty(HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys));
    }

    [Fact]
    public void The_default_now_points_at_the_key_under_escape()
    {
        HotkeyDefinition created = DefaultConfiguration.CreateDropDownConsoleHotkeys().Single();

        Assert.Equal(HotkeyKey.Oem5, created.Key);
    }
}
