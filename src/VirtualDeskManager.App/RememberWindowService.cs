using System;
using System.Collections.Generic;
using System.Linq;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App;

/// <summary>
/// Turns "always open this application like this" into a rule.
/// </summary>
/// <remarks>
/// There is no separate mechanism for it: what the user asks for is exactly a rule, so it is
/// written as one, saved in rules.json and visible and editable in the Rules screen. That way
/// nothing happens behind the user's back, and the rule engine applies it like any other
/// (spec sections 20 and 45).
/// </remarks>
public sealed class RememberWindowService
{
    private readonly AppHost _host;

    /// <summary>Works through the host: its desktops and windows to read the state, its configuration to save the rule.</summary>
    public RememberWindowService(AppHost host) => _host = host;

    /// <summary>Desktop the window is on right now, if it can be worked out.</summary>
    public VirtualDesktopInfo? CurrentDesktopOf(nint hwnd)
    {
        if (!_host.VirtualDesktops.Capabilities.CanEnumerateDesktops)
        {
            return null;
        }

        Guid? id = _host.VirtualDesktops.GetWindowDesktopId(hwnd);

        return id is null
            ? _host.VirtualDesktops.GetCurrentDesktop()
            : _host.VirtualDesktops.GetDesktops().FirstOrDefault(d => d.Id == id.Value);
    }

    /// <summary>Everything the window is doing right now, ready to become a rule.</summary>
    public WindowActions CaptureState(nint hwnd)
    {
        WindowInfo? window = _host.WindowManager.GetWindowInfo(hwnd);
        VirtualDesktopInfo? desktop = CurrentDesktopOf(hwnd);

        return new WindowActions
        {
            Desktop = desktop is null ? null : DesktopResolver.Describe(desktop),
            AlwaysOnTop = window?.IsTopMost == true ? true : null,
            Maximized = window?.IsMaximized == true ? true : null,
            PinWindow = _host.VirtualDesktops.IsWindowPinned(hwnd) == true ? true : null
        };
    }

    /// <summary>
    /// Saves the rule for the application that owns the window, merging with the rule that
    /// already covers it instead of piling up duplicates.
    /// </summary>
    /// <returns>The name shown to the user, or null when the window says nothing about itself.</returns>
    public string? Remember(nint hwnd, WindowActions actions)
    {
        WindowInfo? window = _host.WindowManager.GetWindowInfo(hwnd);

        if (window is null || string.IsNullOrWhiteSpace(window.ProcessName))
        {
            return null;
        }

        List<WindowRule> rules = _host.Configuration.Rules.ToList();
        WindowRule? existing = RuleFactory.FindForApplication(rules, window.ProcessName);

        if (existing is not null)
        {
            RuleFactory.Merge(existing.Actions, actions);
            existing.Enabled = true;
        }
        else
        {
            rules.Add(RuleFactory.ForApplication(window, actions));
        }

        _host.ApplyRules(rules);
        return existing?.Name ?? window.ProcessName;
    }
}
