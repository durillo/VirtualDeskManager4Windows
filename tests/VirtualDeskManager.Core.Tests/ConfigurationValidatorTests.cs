using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class ConfigurationValidatorTests
{
    private static HotkeyDefinition Hotkey(
        string id = "toggle",
        HotkeyModifiers modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        HotkeyKey key = HotkeyKey.T,
        CommandType command = CommandType.ToggleAlwaysOnTop) =>
        new()
        {
            Id = id,
            Modifiers = modifiers,
            Key = key,
            Command = new CommandDefinition(command)
        };

    private static WindowRule Rule(
        string name = "Spotify",
        WindowMatcher? match = null,
        WindowActions? actions = null) =>
        new()
        {
            Name = name,
            Match = match ?? new WindowMatcher { ProcessName = "Spotify.exe" },
            Actions = actions ?? new WindowActions { AlwaysOnTop = true }
        };

    [Fact]
    public void A_complete_shortcut_has_no_issues()
    {
        Assert.Empty(ConfigurationValidator.Validate(Hotkey()));
    }

    /// <summary>
    /// A bare key is allowed. It used to be refused outright, and that was the wrong call: it is
    /// the user's keyboard, and the editor could not even offer what they were asking for.
    /// </summary>
    [Fact]
    public void A_shortcut_without_modifiers_is_allowed()
    {
        Assert.Empty(ConfigurationValidator.Validate(Hotkey(modifiers: HotkeyModifiers.None)));
    }

    /// <summary>But it is not waved through in silence: the cost is spelled out.</summary>
    [Fact]
    public void A_shortcut_without_modifiers_is_warned_about()
    {
        Assert.Contains(
            ValidationIssue.HotkeyNeedsModifier,
            ConfigurationValidator.Warnings(Hotkey(modifiers: HotkeyModifiers.None)));
    }

    /// <summary>
    /// NoRepeat changes how a shortcut behaves while held down, not which keystroke it claims, so
    /// it does not count as a modifier for this purpose.
    /// </summary>
    [Fact]
    public void A_shortcut_with_only_NoRepeat_is_still_a_bare_key()
    {
        Assert.Contains(
            ValidationIssue.HotkeyNeedsModifier,
            ConfigurationValidator.Warnings(Hotkey(modifiers: HotkeyModifiers.NoRepeat)));
    }

    [Fact]
    public void A_shortcut_with_a_modifier_is_not_warned_about()
    {
        Assert.Empty(ConfigurationValidator.Warnings(Hotkey(modifiers: HotkeyModifiers.Control)));
    }

    /// <summary>
    /// The Windows key is allowed, but Windows keeps a great many of its combinations, so the
    /// user is told before they save rather than left wondering why nothing happens.
    /// </summary>
    [Fact]
    public void A_shortcut_using_the_windows_key_is_warned_about()
    {
        Assert.Contains(
            ValidationIssue.HotkeyWindowsKeyOftenReserved,
            ConfigurationValidator.Warnings(Hotkey(modifiers: HotkeyModifiers.Windows)));
    }

    [Fact]
    public void The_windows_key_is_still_a_perfectly_valid_shortcut()
    {
        Assert.Empty(ConfigurationValidator.Validate(
            Hotkey(modifiers: HotkeyModifiers.Windows | HotkeyModifiers.Shift)));
    }

    [Fact]
    public void A_shortcut_without_the_windows_key_gets_no_such_warning()
    {
        Assert.DoesNotContain(
            ValidationIssue.HotkeyWindowsKeyOftenReserved,
            ConfigurationValidator.Warnings(Hotkey(modifiers: HotkeyModifiers.Control | HotkeyModifiers.Alt)));
    }

    [Fact]
    public void A_shortcut_with_no_key_at_all_is_not_warned_about_its_modifiers()
    {
        // There is nothing to take away from anyone yet; the missing key is the real problem and
        // it is already reported as an issue.
        Assert.Empty(ConfigurationValidator.Warnings(
            Hotkey(key: HotkeyKey.None, modifiers: HotkeyModifiers.None)));
    }

    [Fact]
    public void A_shortcut_without_a_key_is_rejected()
    {
        Assert.Contains(
            ValidationIssue.HotkeyNeedsKey,
            ConfigurationValidator.Validate(Hotkey(key: HotkeyKey.None)));
    }

    [Fact]
    public void A_shortcut_without_an_identifier_is_rejected()
    {
        Assert.Contains(ValidationIssue.HotkeyEmptyId, ConfigurationValidator.Validate(Hotkey(id: "  ")));
    }

    [Fact]
    public void A_shortcut_without_a_command_is_rejected()
    {
        Assert.Contains(
            ValidationIssue.HotkeyNoCommand,
            ConfigurationValidator.Validate(Hotkey(command: CommandType.None)));
    }

    [Fact]
    public void Two_shortcuts_cannot_share_an_identifier()
    {
        HotkeyDefinition edited = Hotkey(id: "same", key: HotkeyKey.J);
        HotkeyDefinition existing = Hotkey(id: "SAME", key: HotkeyKey.K);

        Assert.Contains(
            ValidationIssue.HotkeyDuplicateId,
            ConfigurationValidator.Validate(edited, new[] { existing }));
    }

    [Fact]
    public void Two_shortcuts_cannot_share_a_combination()
    {
        HotkeyDefinition edited = Hotkey(id: "one");
        HotkeyDefinition existing = Hotkey(id: "two");

        Assert.Contains(
            ValidationIssue.HotkeyDuplicateCombination,
            ConfigurationValidator.Validate(edited, new[] { existing }));
    }

    [Fact]
    public void A_shortcut_does_not_collide_with_itself()
    {
        HotkeyDefinition hotkey = Hotkey();

        Assert.Empty(ConfigurationValidator.Validate(hotkey, new[] { hotkey }));
    }

    [Fact]
    public void The_same_key_with_different_modifiers_is_not_a_collision()
    {
        HotkeyDefinition edited = Hotkey(id: "one", modifiers: HotkeyModifiers.Control | HotkeyModifiers.Alt);
        HotkeyDefinition existing = Hotkey(
            id: "two",
            modifiers: HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);

        Assert.Empty(ConfigurationValidator.Validate(edited, new[] { existing }));
    }

    [Fact]
    public void A_complete_rule_has_no_issues()
    {
        Assert.Empty(ConfigurationValidator.Validate(Rule()));
    }

    [Fact]
    public void A_rule_without_a_name_is_rejected()
    {
        Assert.Contains(ValidationIssue.RuleNeedsName, ConfigurationValidator.Validate(Rule(name: "")));
    }

    [Fact]
    public void A_rule_without_criteria_is_rejected()
    {
        Assert.Contains(
            ValidationIssue.RuleNeedsCriteria,
            ConfigurationValidator.Validate(Rule(match: new WindowMatcher())));
    }

    [Fact]
    public void A_rule_without_actions_is_rejected()
    {
        Assert.Contains(
            ValidationIssue.RuleNeedsActions,
            ConfigurationValidator.Validate(Rule(actions: new WindowActions())));
    }

    [Fact]
    public void A_broken_regular_expression_is_rejected()
    {
        Assert.Contains(
            ValidationIssue.RuleInvalidRegex,
            ConfigurationValidator.Validate(Rule(match: new WindowMatcher { TitleRegex = "([unclosed" })));
    }

    [Fact]
    public void A_good_regular_expression_is_accepted()
    {
        Assert.Empty(ConfigurationValidator.Validate(Rule(match: new WindowMatcher { TitleRegex = @"^Untitled.*$" })));
    }

    [Fact]
    public void The_suggested_identifier_is_readable_and_unique()
    {
        string first = ConfigurationValidator.SuggestHotkeyId(CommandType.MoveWindowLeft, Array.Empty<HotkeyDefinition>());
        Assert.Equal("move-window-left", first);

        string second = ConfigurationValidator.SuggestHotkeyId(
            CommandType.MoveWindowLeft,
            new[] { Hotkey(id: "move-window-left") });

        Assert.Equal("move-window-left-2", second);
    }
}
