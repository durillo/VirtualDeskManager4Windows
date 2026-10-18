using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Ver y quitar la vista de todos los escritorios.</summary>
public class TaskViewHotkeyTests
{
    [Fact]
    public void There_is_one_shortcut_to_show_and_one_to_hide()
    {
        List<HotkeyDefinition> hotkeys = DefaultConfiguration.CreateTaskViewHotkeys().ToList();

        Assert.Equal(2, hotkeys.Count);
        Assert.Contains(hotkeys, h => h.Command.Type == CommandType.ShowAllDesktops);
        Assert.Contains(hotkeys, h => h.Command.Type == CommandType.HideAllDesktops);
        Assert.All(hotkeys, h => Assert.True(h.IsValid));
    }

    // Arriba para verlo todo y abajo para volver, que es lo que se pidio.
    [Fact]
    public void Showing_is_Win_Ctrl_Up_and_hiding_is_Win_Ctrl_Down()
    {
        List<HotkeyDefinition> hotkeys = DefaultConfiguration.CreateTaskViewHotkeys().ToList();

        HotkeyDefinition ver = hotkeys.Single(h => h.Command.Type == CommandType.ShowAllDesktops);
        HotkeyDefinition quitar = hotkeys.Single(h => h.Command.Type == CommandType.HideAllDesktops);

        Assert.Equal(HotkeyModifiers.Windows | HotkeyModifiers.Control, ver.Modifiers);
        Assert.Equal(HotkeyKey.Up, ver.Key);

        Assert.Equal(HotkeyModifiers.Windows | HotkeyModifiers.Control, quitar.Modifiers);
        Assert.Equal(HotkeyKey.Down, quitar.Key);
    }

    [Fact]
    public void They_are_in_the_defaults_of_a_new_installation()
    {
        List<HotkeyDefinition> defaults = DefaultConfiguration.CreateHotkeys();

        Assert.Contains(defaults, h => h.Command.Type == CommandType.ShowAllDesktops);
        Assert.Contains(defaults, h => h.Command.Type == CommandType.HideAllDesktops);
    }

    [Fact]
    public void They_are_added_to_a_configuration_that_predates_them()
    {
        List<HotkeyDefinition> existing = DefaultConfiguration.CreateHotkeys()
            .Where(h => h.Command.Type is not (CommandType.ShowAllDesktops or CommandType.HideAllDesktops))
            .ToList();

        Assert.Equal(2, HotkeyMigrations.AddTaskViewHotkeys(existing).Count);
    }

    [Fact]
    public void Running_the_migration_twice_does_not_duplicate_them()
    {
        List<HotkeyDefinition> hotkeys = DefaultConfiguration.CreateHotkeys();

        Assert.Empty(HotkeyMigrations.AddTaskViewHotkeys(hotkeys));
    }

    [Fact]
    public void A_combination_the_user_already_uses_is_left_alone()
    {
        List<HotkeyDefinition> hotkeys = new()
        {
            new HotkeyDefinition
            {
                Id = "mine",
                Modifiers = HotkeyModifiers.Windows | HotkeyModifiers.Control,
                Key = HotkeyKey.Up,
                Command = new CommandDefinition(CommandType.ToggleAlwaysOnTop)
            }
        };

        IReadOnlyList<HotkeyDefinition> added = HotkeyMigrations.AddTaskViewHotkeys(hotkeys);

        Assert.Single(added);
        Assert.Equal(CommandType.HideAllDesktops, added[0].Command.Type);
    }
}
