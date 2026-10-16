using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>Pure matching + merging logic. No Win32, therefore fully unit testable (spec section 40).</summary>
public interface IRuleEngine
{
    /// <summary>The current rule set, in declaration order, enabled or not.</summary>
    IReadOnlyList<WindowRule> Rules { get; }

    /// <summary>Rules whose regular expression could not be compiled; surfaced by the UI.</summary>
    IReadOnlyList<WindowRule> InvalidRules { get; }

    /// <summary>
    /// Replaces the whole rule set. <see cref="InvalidRules"/> starts empty again and fills as
    /// windows are evaluated.
    /// </summary>
    void SetRules(IEnumerable<WindowRule> rules);

    /// <summary>
    /// Which enabled rules match <paramref name="window"/> and what they ask for once merged by
    /// priority.
    /// </summary>
    RuleEvaluationResult Evaluate(WindowInfo window);
}
