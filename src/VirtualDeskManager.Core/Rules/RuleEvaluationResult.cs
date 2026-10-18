namespace VirtualDeskManager.Core.Rules;

/// <summary>Outcome of evaluating the rule set against one window.</summary>
public sealed class RuleEvaluationResult
{
    /// <summary>No rule matched. Shared, so it must not be modified.</summary>
    public static RuleEvaluationResult Empty { get; } =
        new(Array.Empty<WindowRule>(), new ResolvedWindowActions());

    /// <summary>Wraps the matching rules and their merged actions.</summary>
    public RuleEvaluationResult(IReadOnlyList<WindowRule> matchedRules, ResolvedWindowActions actions)
    {
        MatchedRules = matchedRules;
        Actions = actions;
    }

    /// <summary>Matching rules, highest priority first.</summary>
    public IReadOnlyList<WindowRule> MatchedRules { get; }

    /// <summary>What the matching rules ask for, merged by priority.</summary>
    public ResolvedWindowActions Actions { get; }

    /// <summary>Whether any enabled rule matched the window.</summary>
    public bool HasMatches => MatchedRules.Count > 0;

    /// <summary>Whether there is anything to apply. A rule can match and still ask for nothing.</summary>
    public bool HasActions => Actions.HasAny;
}
