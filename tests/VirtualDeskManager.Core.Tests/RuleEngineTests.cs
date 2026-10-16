using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class RuleEngineTests
{
    private static WindowRule Rule(
        string name,
        int priority = 0,
        bool enabled = true,
        WindowMatcher? match = null,
        WindowActions? actions = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Priority = priority,
            Enabled = enabled,
            Match = match ?? new WindowMatcher { ProcessName = "notepad.exe" },
            Actions = actions ?? new WindowActions { AlwaysOnTop = true }
        };

    [Fact]
    public void Matches_by_process_name_ignoring_case_and_extension()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("notepad", match: new WindowMatcher { ProcessName = "NOTEPAD" }) });

        RuleEvaluationResult result = engine.Evaluate(TestWindows.Create());

        Assert.True(result.HasMatches);
        Assert.True(result.Actions.AlwaysOnTop);
    }

    [Fact]
    public void Does_not_match_a_different_process()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("spotify", match: new WindowMatcher { ProcessName = "Spotify.exe" }) });

        RuleEvaluationResult result = engine.Evaluate(TestWindows.Create());

        Assert.False(result.HasMatches);
        Assert.False(result.HasActions);
    }

    [Fact]
    public void Matches_by_title_fragment()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("title", match: new WindowMatcher { TitleContains = "untitled" }) });

        Assert.True(engine.Evaluate(TestWindows.Create()).HasMatches);
    }

    [Fact]
    public void Matches_by_title_regex()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("regex", match: new WindowMatcher { TitleRegex = @"^Untitled\s-\s.*$" }) });

        Assert.True(engine.Evaluate(TestWindows.Create()).HasMatches);
    }

    [Fact]
    public void An_invalid_regex_never_matches_and_is_reported()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("broken", match: new WindowMatcher { TitleRegex = "([unclosed" }) });

        Assert.False(engine.Evaluate(TestWindows.Create()).HasMatches);
        Assert.Single(engine.InvalidRules);
    }

    [Fact]
    public void All_criteria_must_match()
    {
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            Rule("both", match: new WindowMatcher { ProcessName = "notepad.exe", TitleContains = "does not appear" })
        });

        Assert.False(engine.Evaluate(TestWindows.Create()).HasMatches);
    }

    [Fact]
    public void A_matcher_without_criteria_matches_nothing()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("empty", match: new WindowMatcher()) });

        Assert.False(engine.Evaluate(TestWindows.Create()).HasMatches);
    }

    [Fact]
    public void Disabled_rules_are_ignored()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("off", enabled: false) });

        Assert.False(engine.Evaluate(TestWindows.Create()).HasMatches);
    }

    [Fact]
    public void The_highest_priority_wins_for_the_same_property()
    {
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            Rule("low", priority: 50, actions: new WindowActions { AlwaysOnTop = false }),
            Rule("high", priority: 100, actions: new WindowActions { AlwaysOnTop = true })
        });

        RuleEvaluationResult result = engine.Evaluate(TestWindows.Create());

        Assert.True(result.Actions.AlwaysOnTop);
        Assert.Equal("high", result.Actions.Sources["AlwaysOnTop"]);
    }

    [Fact]
    public void With_the_same_priority_the_first_declared_rule_wins()
    {
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            Rule("first", priority: 10, actions: new WindowActions { AlwaysOnTop = true }),
            Rule("second", priority: 10, actions: new WindowActions { AlwaysOnTop = false })
        });

        RuleEvaluationResult result = engine.Evaluate(TestWindows.Create());

        Assert.True(result.Actions.AlwaysOnTop);
        Assert.Equal("first", result.Actions.Sources["AlwaysOnTop"]);
    }

    [Fact]
    public void Actions_of_different_rules_are_merged()
    {
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            Rule("top", priority: 10, actions: new WindowActions { AlwaysOnTop = true }),
            Rule("max", priority: 5, actions: new WindowActions { Maximized = true })
        });

        RuleEvaluationResult result = engine.Evaluate(TestWindows.Create());

        Assert.True(result.Actions.AlwaysOnTop);
        Assert.True(result.Actions.Maximized);
        Assert.Equal(2, result.MatchedRules.Count);
    }

    [Fact]
    public void Matched_rules_are_ordered_by_descending_priority()
    {
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            Rule("low", priority: 1),
            Rule("high", priority: 99)
        });

        RuleEvaluationResult result = engine.Evaluate(TestWindows.Create());

        Assert.Equal("high", result.MatchedRules[0].Name);
        Assert.Equal("low", result.MatchedRules[1].Name);
    }

    [Fact]
    public void Matches_an_executable_path_with_wildcards()
    {
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            Rule("path", match: new WindowMatcher { ExecutablePath = @"C:\Windows\*\notepad.exe" })
        });

        Assert.True(engine.Evaluate(TestWindows.Create()).HasMatches);
    }

    [Fact]
    public void Matches_by_window_class()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("class", match: new WindowMatcher { WindowClass = "notepad" }) });

        Assert.True(engine.Evaluate(TestWindows.Create()).HasMatches);
    }

    [Fact]
    public void The_signature_only_changes_when_the_actions_change()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { Rule("top") });

        string first = engine.Evaluate(TestWindows.Create()).Actions.ComputeSignature();
        string second = engine.Evaluate(TestWindows.Create(title: "another title")).Actions.ComputeSignature();

        Assert.Equal(first, second);
    }
}
