using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// La segunda combinación de un atajo: lanzar la misma acción de dos maneras, por ejemplo con el
/// 4 de arriba y con el 4 del teclado numérico.
/// </summary>
public class AlternateCombinationTests
{
    private static HotkeyDefinition Hotkey(
        string id,
        HotkeyKey key,
        HotkeyModifiers modifiers = HotkeyModifiers.Control,
        HotkeyKey alternateKey = HotkeyKey.None,
        HotkeyModifiers alternateModifiers = HotkeyModifiers.Control) =>
        new()
        {
            Id = id,
            Key = key,
            Modifiers = modifiers,
            AlternateKey = alternateKey,
            AlternateModifiers = alternateKey == HotkeyKey.None ? HotkeyModifiers.None : alternateModifiers,
            Command = new CommandDefinition(CommandType.ToggleAlwaysOnTop)
        };

    [Fact]
    public void A_shortcut_without_a_second_one_claims_a_single_keystroke()
    {
        HotkeyDefinition hotkey = Hotkey("a", HotkeyKey.F4);

        Assert.False(hotkey.HasAlternate);
        Assert.Single(hotkey.Combinations);
    }

    [Fact]
    public void A_shortcut_with_a_second_one_claims_two()
    {
        HotkeyDefinition hotkey = Hotkey("a", HotkeyKey.F4, alternateKey: HotkeyKey.NumPad4);

        Assert.True(hotkey.HasAlternate);
        Assert.Equal(2, hotkey.Combinations.Count());
    }

    [Fact]
    public void Both_combinations_are_shown_so_the_list_does_not_hide_one()
    {
        HotkeyDefinition hotkey = Hotkey("a", HotkeyKey.F4, alternateKey: HotkeyKey.NumPad4);

        Assert.Contains("F4", hotkey.DisplayText);
        Assert.Contains("NumPad4", hotkey.DisplayText);
    }

    /// <summary>NoRepeat cambia cómo se comporta mantenida, no qué pulsación reclama.</summary>
    [Fact]
    public void NoRepeat_does_not_make_two_combinations_different()
    {
        HotkeyCombination plain = new(HotkeyModifiers.Control, HotkeyKey.F4);
        HotkeyCombination repeating = new(HotkeyModifiers.Control | HotkeyModifiers.NoRepeat, HotkeyKey.F4);

        Assert.True(plain.SameAs(repeating));
    }

    [Fact]
    public void An_empty_combination_never_clashes_with_anything()
    {
        Assert.False(HotkeyCombination.None.SameAs(HotkeyCombination.None));
        Assert.False(HotkeyCombination.None.SameAs(new HotkeyCombination(HotkeyModifiers.Control, HotkeyKey.F4)));
    }

    // ------------------------------------------------------------------ conflictos

    [Fact]
    public void The_second_combination_of_another_shortcut_is_also_taken()
    {
        HotkeyDefinition taken = Hotkey("mine", HotkeyKey.F4, alternateKey: HotkeyKey.NumPad4);
        HotkeyDefinition wanted = Hotkey("new", HotkeyKey.NumPad4);

        Assert.Same(taken, ConfigurationValidator.FindConflict(wanted, new[] { taken }));
    }

    [Fact]
    public void My_second_combination_clashing_with_someone_elses_first_is_a_conflict()
    {
        HotkeyDefinition taken = Hotkey("theirs", HotkeyKey.NumPad4);
        HotkeyDefinition wanted = Hotkey("new", HotkeyKey.F4, alternateKey: HotkeyKey.NumPad4);

        Assert.Same(taken, ConfigurationValidator.FindConflict(wanted, new[] { taken }));
        Assert.Contains(
            ValidationIssue.HotkeyDuplicateCombination,
            ConfigurationValidator.Validate(wanted, new[] { taken }));
    }

    [Fact]
    public void Two_shortcuts_whose_second_combinations_collide_are_in_conflict()
    {
        HotkeyDefinition taken = Hotkey("theirs", HotkeyKey.A, alternateKey: HotkeyKey.NumPad4);
        HotkeyDefinition wanted = Hotkey("new", HotkeyKey.B, alternateKey: HotkeyKey.NumPad4);

        Assert.Same(taken, ConfigurationValidator.FindConflict(wanted, new[] { taken }));
    }

    [Fact]
    public void A_shortcut_is_never_in_conflict_with_itself()
    {
        HotkeyDefinition hotkey = Hotkey("a", HotkeyKey.F4, alternateKey: HotkeyKey.NumPad4);

        Assert.Null(ConfigurationValidator.FindConflict(hotkey, new[] { hotkey }));
    }

    /// <summary>
    /// El editor pregunta por una casilla concreta, para poder decir cuál de las dos está ocupada
    /// en vez de un "hay un conflicto" que obliga a buscarlo.
    /// </summary>
    [Fact]
    public void A_single_combination_can_be_asked_about_on_its_own()
    {
        HotkeyDefinition taken = Hotkey("theirs", HotkeyKey.A, alternateKey: HotkeyKey.NumPad4);
        HotkeyDefinition[] others = { taken };

        Assert.Same(
            taken,
            ConfigurationValidator.FindConflict(new HotkeyCombination(HotkeyModifiers.Control, HotkeyKey.NumPad4), others));

        Assert.Null(
            ConfigurationValidator.FindConflict(new HotkeyCombination(HotkeyModifiers.Control, HotkeyKey.Z), others));

        Assert.Null(ConfigurationValidator.FindConflict(HotkeyCombination.None, others));
    }

    // ------------------------------------------------------------------ validación

    [Fact]
    public void Putting_the_same_combination_in_both_boxes_is_pointless_and_is_said()
    {
        HotkeyDefinition hotkey = Hotkey("a", HotkeyKey.F4, alternateKey: HotkeyKey.F4);

        Assert.Contains(ValidationIssue.HotkeySameCombinationTwice, ConfigurationValidator.Validate(hotkey));
    }

    [Fact]
    public void Two_different_combinations_are_fine()
    {
        HotkeyDefinition hotkey = Hotkey("a", HotkeyKey.F4, alternateKey: HotkeyKey.NumPad4);

        Assert.DoesNotContain(ValidationIssue.HotkeySameCombinationTwice, ConfigurationValidator.Validate(hotkey));
    }

    [Fact]
    public void A_bare_second_combination_is_warned_about_just_like_a_bare_first_one()
    {
        HotkeyDefinition hotkey = Hotkey(
            "a",
            HotkeyKey.F4,
            alternateKey: HotkeyKey.NumPad4,
            alternateModifiers: HotkeyModifiers.None);

        Assert.Contains(ValidationIssue.HotkeyNeedsModifier, ConfigurationValidator.Warnings(hotkey));
    }

    [Fact]
    public void A_second_combination_on_the_windows_key_is_warned_about_too()
    {
        HotkeyDefinition hotkey = Hotkey(
            "a",
            HotkeyKey.F4,
            alternateKey: HotkeyKey.NumPad4,
            alternateModifiers: HotkeyModifiers.Windows);

        Assert.Contains(ValidationIssue.HotkeyWindowsKeyOftenReserved, ConfigurationValidator.Warnings(hotkey));
    }

    [Fact]
    public void A_perfectly_ordinary_pair_gets_no_warnings()
    {
        HotkeyDefinition hotkey = Hotkey("a", HotkeyKey.F4, alternateKey: HotkeyKey.NumPad4);

        Assert.Empty(ConfigurationValidator.Warnings(hotkey));
    }

    // ------------------------------------------------------------------ fichero

    [Fact]
    public void A_shortcut_without_a_second_one_is_written_exactly_as_before()
    {
        HotkeyEntryFile file = ConfigurationMapper.ToFile(Hotkey("a", HotkeyKey.F4));

        Assert.Null(file.AlternateKey);
        Assert.Null(file.AlternateModifiers);
    }

    [Fact]
    public void A_second_combination_survives_a_round_trip_through_the_file()
    {
        HotkeyDefinition original = Hotkey(
            "a",
            HotkeyKey.F4,
            HotkeyModifiers.Control | HotkeyModifiers.Shift,
            HotkeyKey.NumPad4,
            HotkeyModifiers.Alt);

        HotkeyDefinition? read = ConfigurationMapper.ToDefinition(ConfigurationMapper.ToFile(original), out string? error);

        Assert.Null(error);
        Assert.NotNull(read);
        Assert.Equal(original.Primary, read!.Primary);
        Assert.Equal(original.Alternate, read.Alternate);
    }

    [Fact]
    public void A_file_with_an_unknown_second_key_is_reported_and_not_guessed_at()
    {
        HotkeyEntryFile entry = new()
        {
            Id = "a",
            Modifiers = new List<string> { "Control" },
            Key = "F4",
            AlternateKey = "Banana",
            Command = new CommandFile { Type = "ToggleAlwaysOnTop" }
        };

        Assert.Null(ConfigurationMapper.ToDefinition(entry, out string? error));
        Assert.Contains("Banana", error);
    }
}
