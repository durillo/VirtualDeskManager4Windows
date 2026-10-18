using System.Collections.Generic;
using System.Linq;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App.Localization;

/// <summary>
/// Turns domain values into text for the user. It lives in the application layer because Core
/// must not contain interface strings; Core only exposes enums and translation keys.
/// </summary>
public static class UiText
{
    /// <summary>The translated name of a command, from the key <c>Command.&lt;type&gt;</c>.</summary>
    public static string Command(CommandType type) => Loc.Get("Command." + type);

    /// <summary>
    /// The action of a shortcut, including which desktop it acts on when it acts on one.
    /// </summary>
    /// <remarks>
    /// Without the number the nine Ctrl+Alt+1..9 shortcuts all read the same, and the list stops
    /// being editable in any useful sense: the user cannot tell which row is which.
    /// </remarks>
    public static string Command(CommandDefinition command)
    {
        string text = Command(command.Type);

        return command.Type is CommandType.SwitchToDesktop or CommandType.MoveWindowToDesktop
            && command.TryGetInt("number", out int number)
                ? $"{text} {number}"
                : text;
    }

    /// <summary>The translated name of an operation status, from the key <c>Status.&lt;status&gt;</c>.</summary>
    public static string Status(OperationStatus status) => Loc.Get("Status." + status);

    /// <summary>"Yes" or "No" in the current language.</summary>
    public static string YesNo(bool value) => Loc.Get(value ? "Common.Yes" : "Common.No");

    /// <summary>Human readable outcome of an operation, with the technical detail when there is one.</summary>
    public static string Describe(OperationResult result) =>
        string.IsNullOrWhiteSpace(result.Error)
            ? Status(result.Status)
            : Status(result.Status) + " " + result.Error;

    /// <summary>
    /// The conditions of a rule in one line: process, path, title and class, joined with "and".
    /// Only the conditions that are filled in appear.
    /// </summary>
    public static string DescribeMatch(WindowMatcher matcher)
    {
        List<string> parts = new();

        if (!string.IsNullOrWhiteSpace(matcher.ProcessName))
        {
            parts.Add(Loc.Format("Match.Process", matcher.ProcessName));
        }

        if (!string.IsNullOrWhiteSpace(matcher.ExecutablePath))
        {
            parts.Add(Loc.Format("Match.Path", matcher.ExecutablePath));
        }

        if (!string.IsNullOrWhiteSpace(matcher.TitleContains))
        {
            parts.Add(Loc.Format("Match.TitleContains", matcher.TitleContains));
        }

        if (!string.IsNullOrWhiteSpace(matcher.TitleRegex))
        {
            parts.Add(Loc.Format("Match.TitleMatches", matcher.TitleRegex));
        }

        if (!string.IsNullOrWhiteSpace(matcher.WindowClass))
        {
            parts.Add(Loc.Format("Match.Class", matcher.WindowClass));
        }

        return parts.Count == 0 ? Loc.Get("Match.None") : string.Join(Loc.Get("Match.And"), parts);
    }

    /// <summary>What a rule does to the window, as a comma separated list. Actions left unset are skipped.</summary>
    public static string DescribeActions(WindowActions actions)
    {
        List<string> parts = new();

        if (actions.AlwaysOnTop is bool alwaysOnTop)
        {
            parts.Add(Loc.Get(alwaysOnTop ? "Act.AlwaysOnTopOn" : "Act.AlwaysOnTopOff"));
        }

        if (actions.PinWindow is bool pinWindow)
        {
            parts.Add(Loc.Get(pinWindow ? "Act.PinWindowOn" : "Act.PinWindowOff"));
        }

        if (actions.PinApplication is bool pinApplication)
        {
            parts.Add(Loc.Get(pinApplication ? "Act.PinApplicationOn" : "Act.PinApplicationOff"));
        }

        if (actions.Desktop is not null)
        {
            parts.Add(Loc.Format("Act.Desktop", actions.Desktop));
        }

        if (actions.Monitor is int monitor)
        {
            parts.Add(Loc.Format("Act.Monitor", monitor));
        }

        if (actions.Maximized is bool maximized)
        {
            parts.Add(Loc.Get(maximized ? "Act.Maximized" : "Act.Restored"));
        }

        if (actions.Bounds is WindowBounds bounds)
        {
            parts.Add(Loc.Format("Act.Bounds", bounds.ToString()));
        }

        return parts.Count == 0 ? Loc.Get("Act.None") : string.Join(", ", parts);
    }
}
