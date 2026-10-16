using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>
/// Builds a rule out of a window that is already on screen, which is how "always open this
/// application like this" is expressed: as a rule the user can then read and edit.
/// </summary>
public static class RuleFactory
{
    /// <summary>Rule that matches every window of the same application.</summary>
    public static WindowRule ForApplication(WindowInfo window, WindowActions actions)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(actions);

        return new WindowRule
        {
            Id = Guid.NewGuid(),
            Name = FriendlyName(window),
            Enabled = true,
            Priority = 100,
            Match = new WindowMatcher { ProcessName = window.ProcessName },
            Actions = actions
        };
    }

    /// <summary>
    /// The rule that already covers this application, if there is one. Only a rule whose single
    /// criterion is the process name counts: anything more specific belongs to the user and must
    /// not be quietly rewritten.
    /// </summary>
    public static WindowRule? FindForApplication(IEnumerable<WindowRule> rules, string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        return rules.FirstOrDefault(rule =>
            !string.IsNullOrWhiteSpace(rule.Match.ProcessName)
            && string.IsNullOrWhiteSpace(rule.Match.ExecutablePath)
            && string.IsNullOrWhiteSpace(rule.Match.TitleContains)
            && string.IsNullOrWhiteSpace(rule.Match.TitleRegex)
            && string.IsNullOrWhiteSpace(rule.Match.WindowClass)
            && RuleEngine.ProcessNameMatches(rule.Match.ProcessName!, processName));
    }

    /// <summary>
    /// Adds the new actions to the ones the rule already had, leaving alone whatever the new
    /// ones say nothing about.
    /// </summary>
    public static void Merge(WindowActions target, WindowActions extra)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(extra);

        if (extra.Desktop is not null)
        {
            target.Desktop = extra.Desktop;
        }

        if (extra.AlwaysOnTop is not null)
        {
            target.AlwaysOnTop = extra.AlwaysOnTop;
        }

        if (extra.PinWindow is not null)
        {
            target.PinWindow = extra.PinWindow;
        }

        if (extra.PinApplication is not null)
        {
            target.PinApplication = extra.PinApplication;
        }

        if (extra.Monitor is not null)
        {
            target.Monitor = extra.Monitor;
        }

        if (extra.Maximized is not null)
        {
            target.Maximized = extra.Maximized;
        }

        if (extra.Bounds is not null)
        {
            target.Bounds = extra.Bounds;
        }
    }

    /// <summary>"Spotify.exe" reads better as "Spotify".</summary>
    private static string FriendlyName(WindowInfo window)
    {
        string name = window.ProcessName;

        if (string.IsNullOrWhiteSpace(name))
        {
            return window.Title.Length > 0 ? window.Title : "Rule";
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
