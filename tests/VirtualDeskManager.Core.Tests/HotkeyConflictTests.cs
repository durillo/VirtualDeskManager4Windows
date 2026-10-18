using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// Finding the shortcut that already owns a combination, so the editor can say what it would be
/// taking it from instead of just refusing.
/// </summary>
public class HotkeyConflictTests
{
    private static HotkeyDefinition Hotkey(
        string id,
        HotkeyKey key,
        HotkeyModifiers modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        CommandType command = CommandType.ToggleAlwaysOnTop) =>
        new()
        {
            Id = id,
            Key = key,
            Modifiers = modifiers,
            Command = new CommandDefinition(command)
        };

    [Fact]
    public void A_free_combination_has_no_conflict()
    {
        HotkeyDefinition[] others = { Hotkey("a", HotkeyKey.T) };

        Assert.Null(ConfigurationValidator.FindConflict(Hotkey("b", HotkeyKey.P), others));
    }

    [Fact]
    public void The_shortcut_already_using_it_comes_back()
    {
        HotkeyDefinition taken = Hotkey("desktop-left", HotkeyKey.Left, command: CommandType.SwitchDesktopLeft);
        HotkeyDefinition[] others = { Hotkey("other", HotkeyKey.T), taken };

        HotkeyDefinition? found = ConfigurationValidator.FindConflict(Hotkey("new", HotkeyKey.Left), others);

        Assert.Same(taken, found);
    }

    [Fact]
    public void The_same_key_with_different_modifiers_is_not_a_conflict()
    {
        HotkeyDefinition[] others = { Hotkey("a", HotkeyKey.Left, HotkeyModifiers.Control | HotkeyModifiers.Alt) };

        Assert.Null(ConfigurationValidator.FindConflict(
            Hotkey("b", HotkeyKey.Left, HotkeyModifiers.Control | HotkeyModifiers.Shift),
            others));
    }

    /// <summary>
    /// NoRepeat says what happens while the key is held, not which keystroke is claimed, so two
    /// shortcuts differing only in that still collide.
    /// </summary>
    [Fact]
    public void NoRepeat_does_not_make_a_combination_distinct()
    {
        HotkeyDefinition[] others =
        {
            Hotkey("a", HotkeyKey.Left, HotkeyModifiers.Control | HotkeyModifiers.Alt)
        };

        HotkeyDefinition candidate = Hotkey(
            "b",
            HotkeyKey.Left,
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat);

        Assert.NotNull(ConfigurationValidator.FindConflict(candidate, others));
    }

    [Fact]
    public void A_shortcut_does_not_conflict_with_itself()
    {
        HotkeyDefinition shortcut = Hotkey("a", HotkeyKey.Left);

        Assert.Null(ConfigurationValidator.FindConflict(shortcut, new[] { shortcut }));
    }

    [Fact]
    public void Without_a_key_there_is_nothing_to_clash_with()
    {
        HotkeyDefinition[] others = { Hotkey("a", HotkeyKey.None) };

        Assert.Null(ConfigurationValidator.FindConflict(Hotkey("b", HotkeyKey.None), others));
    }

    [Fact]
    public void A_bare_key_can_conflict_with_another_bare_key()
    {
        HotkeyDefinition taken = Hotkey("a", HotkeyKey.F12, HotkeyModifiers.None);
        HotkeyDefinition[] others = { taken };

        Assert.Same(
            taken,
            ConfigurationValidator.FindConflict(Hotkey("b", HotkeyKey.F12, HotkeyModifiers.None), others));
    }

    /// <summary>
    /// The OEM keys were missing from the enum, which is what made the editor ignore the º key on
    /// a Spanish keyboard. They have to be nameable for a shortcut to be stored at all.
    /// </summary>
    [Theory]
    [InlineData(HotkeyKey.Oem1, 0xBA)]
    [InlineData(HotkeyKey.Oem2, 0xBF)]
    [InlineData(HotkeyKey.OemTilde, 0xC0)]
    [InlineData(HotkeyKey.Oem4, 0xDB)]
    [InlineData(HotkeyKey.Oem5, 0xDC)]
    [InlineData(HotkeyKey.Oem6, 0xDD)]
    [InlineData(HotkeyKey.Oem7, 0xDE)]
    [InlineData(HotkeyKey.Oem8, 0xDF)]
    [InlineData(HotkeyKey.Oem102, 0xE2)]
    public void The_oem_keys_carry_their_virtual_key_code(HotkeyKey key, int code)
    {
        Assert.Equal(code, (int)key);
    }

    [Fact]
    public void A_key_with_no_name_is_still_shown_as_something_readable()
    {
        // 0xFF is not a key this enum names, and a shortcut on it must not print as a bare digit
        // sitting next to the modifiers.
        string text = HotkeyDefinition.Format(HotkeyModifiers.Control, (HotkeyKey)0xFF);

        Assert.Contains("Ctrl", text);
        Assert.Contains("0xFF", text);
    }

    [Fact]
    public void The_interface_can_supply_the_name_printed_on_the_key()
    {
        HotkeyDefinition.KeyNameResolver = k => k == HotkeyKey.Oem5 ? "º" : null;

        try
        {
            Assert.Equal("Ctrl + Shift + º", HotkeyDefinition.Format(
                HotkeyModifiers.Control | HotkeyModifiers.Shift,
                HotkeyKey.Oem5));

            // Anything the resolver declines to name falls back to the enum name.
            Assert.Equal("Ctrl + T", HotkeyDefinition.Format(HotkeyModifiers.Control, HotkeyKey.T));
        }
        finally
        {
            HotkeyDefinition.KeyNameResolver = null;
        }
    }

    [Fact]
    public void A_bare_key_is_a_valid_shortcut()
    {
        Assert.True(Hotkey("a", HotkeyKey.F12, HotkeyModifiers.None).IsValid);
    }

    [Fact]
    public void A_shortcut_with_no_key_is_not_valid()
    {
        Assert.False(Hotkey("a", HotkeyKey.None, HotkeyModifiers.Control).IsValid);
    }
}
