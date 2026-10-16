using System.Text;
using System.Text.RegularExpressions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>
/// Matches windows against the rule set and merges the actions of every match by priority
/// (spec sections 20 to 22).
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, Regex?> _regexCache = new(StringComparer.Ordinal);

    private IReadOnlyList<WindowRule> _rules = Array.Empty<WindowRule>();
    private IReadOnlyList<WindowRule> _invalidRules = Array.Empty<WindowRule>();

    /// <summary>Creates an engine with no rules.</summary>
    public RuleEngine(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <inheritdoc/>
    public IReadOnlyList<WindowRule> Rules
    {
        get
        {
            lock (_gate)
            {
                return _rules;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<WindowRule> InvalidRules
    {
        get
        {
            lock (_gate)
            {
                return _invalidRules;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>Also drops the compiled regular expressions, so an edited pattern is compiled afresh.</remarks>
    public void SetRules(IEnumerable<WindowRule> rules)
    {
        List<WindowRule> snapshot = rules.ToList();

        lock (_gate)
        {
            _rules = snapshot;
            _invalidRules = Array.Empty<WindowRule>();
            _regexCache.Clear();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// For each property the highest priority rule that sets it wins; on equal priority, the one
    /// declared first.
    /// </remarks>
    public RuleEvaluationResult Evaluate(WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);

        IReadOnlyList<WindowRule> rules;

        lock (_gate)
        {
            rules = _rules;
        }

        if (rules.Count == 0)
        {
            return RuleEvaluationResult.Empty;
        }

        List<WindowRule> matched = new();

        foreach (WindowRule rule in rules)
        {
            if (!rule.Enabled)
            {
                continue;
            }

            if (Matches(rule, window))
            {
                matched.Add(rule);
            }
        }

        if (matched.Count == 0)
        {
            return RuleEvaluationResult.Empty;
        }

        // OrderByDescending is a stable sort: for equal priority the declaration order decides.
        List<WindowRule> ordered = matched.OrderByDescending(r => r.Priority).ToList();

        ResolvedWindowActions resolved = new();

        foreach (WindowRule rule in ordered)
        {
            Merge(resolved, rule);
        }

        return new RuleEvaluationResult(ordered, resolved);
    }

    private static void Merge(ResolvedWindowActions target, WindowRule rule)
    {
        WindowActions actions = rule.Actions;

        if (actions.AlwaysOnTop is not null && target.AlwaysOnTop is null)
        {
            target.AlwaysOnTop = actions.AlwaysOnTop;
            target.Sources[nameof(target.AlwaysOnTop)] = rule.Name;
        }

        if (actions.PinWindow is not null && target.PinWindow is null)
        {
            target.PinWindow = actions.PinWindow;
            target.Sources[nameof(target.PinWindow)] = rule.Name;
        }

        if (actions.PinApplication is not null && target.PinApplication is null)
        {
            target.PinApplication = actions.PinApplication;
            target.Sources[nameof(target.PinApplication)] = rule.Name;
        }

        if (actions.Desktop is not null && target.Desktop is null)
        {
            target.Desktop = actions.Desktop;
            target.Sources[nameof(target.Desktop)] = rule.Name;
        }

        if (actions.Monitor is not null && target.Monitor is null)
        {
            target.Monitor = actions.Monitor;
            target.Sources[nameof(target.Monitor)] = rule.Name;
        }

        if (actions.Maximized is not null && target.Maximized is null)
        {
            target.Maximized = actions.Maximized;
            target.Sources[nameof(target.Maximized)] = rule.Name;
        }

        if (actions.NewInstance is not null && target.NewInstance is null)
        {
            target.NewInstance = actions.NewInstance;
            target.Sources[nameof(target.NewInstance)] = rule.Name;
        }

        if (actions.ReuseExistingWindow is not null && target.ReuseExistingWindow is null)
        {
            target.ReuseExistingWindow = actions.ReuseExistingWindow;
            target.Sources[nameof(target.ReuseExistingWindow)] = rule.Name;
        }

        if (actions.Bounds is not null && target.Bounds is null)
        {
            target.Bounds = actions.Bounds;
            target.Sources[nameof(target.Bounds)] = rule.Name;
        }
    }

    private bool Matches(WindowRule rule, WindowInfo window)
    {
        WindowMatcher matcher = rule.Match;

        if (!matcher.HasAnyCriteria)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.ProcessName)
            && !ProcessNameMatches(matcher.ProcessName, window.ProcessName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.ExecutablePath)
            && !PathMatches(matcher.ExecutablePath, window.ExecutablePath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.TitleContains)
            && window.Title.IndexOf(matcher.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.WindowClass)
            && !string.Equals(matcher.WindowClass, window.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(matcher.TitleRegex))
        {
            Regex? regex = GetRegex(rule, matcher.TitleRegex);

            if (regex is null)
            {
                return false;
            }

            try
            {
                if (!regex.IsMatch(window.Title))
                {
                    return false;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.Warn(LogEvents.RuleInvalid, "Regex evaluation timed out.", ("Rule", rule.Name));
                return false;
            }
        }

        return true;
    }

    /// <summary>"Spotify.exe", "spotify.exe" and "Spotify" are all the same process.</summary>
    public static bool ProcessNameMatches(string pattern, string actual) =>
        string.Equals(StripExecutableExtension(pattern), StripExecutableExtension(actual), StringComparison.OrdinalIgnoreCase);

    private static string StripExecutableExtension(string value) =>
        value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;

    /// <summary>Full path comparison, with optional <c>*</c> and <c>?</c> wildcards.</summary>
    internal static bool PathMatches(string pattern, string actual)
    {
        if (string.IsNullOrEmpty(actual))
        {
            return false;
        }

        if (pattern.Contains('*', StringComparison.Ordinal) || pattern.Contains('?', StringComparison.Ordinal))
        {
            return Regex.IsMatch(actual, WildcardToRegex(pattern), RegexOptions.IgnoreCase, RegexTimeout);
        }

        return string.Equals(pattern, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string WildcardToRegex(string pattern)
    {
        StringBuilder builder = new("^");

        foreach (char c in pattern)
        {
            builder.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString())
            });
        }

        return builder.Append('$').ToString();
    }

    private Regex? GetRegex(WindowRule rule, string pattern)
    {
        lock (_gate)
        {
            if (_regexCache.TryGetValue(pattern, out Regex? cached))
            {
                return cached;
            }

            Regex? regex;

            try
            {
                regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                regex = null;

                _logger.Warn(
                    LogEvents.RuleInvalid,
                    "The title regular expression could not be compiled; the rule will never match.",
                    ("Rule", rule.Name),
                    ("Pattern", pattern),
                    ("Error", ex.Message));

                if (!_invalidRules.Contains(rule))
                {
                    _invalidRules = _invalidRules.Append(rule).ToList();
                }
            }

            _regexCache[pattern] = regex;
            return regex;
        }
    }
}
