namespace VirtualDeskManager.Core.Commands;

/// <summary>Where a command came from. Logged with every dispatch.</summary>
public enum CommandSource
{
    /// <summary>A global hotkey.</summary>
    Hotkey,
    /// <summary>The tray icon menu.</summary>
    Tray,
    /// <summary>A window or menu of the application itself.</summary>
    UserInterface,
    /// <summary>A window rule applied automatically.</summary>
    Rule
}

/// <summary>
/// Execution context of a command. <see cref="TargetWindow"/> is zero when the command should
/// act on the foreground window, which is the normal case for hotkeys.
/// </summary>
public sealed record CommandContext(CommandSource Source, nint TargetWindow = 0)
{
    /// <summary>Context for a hotkey: it acts on the foreground window.</summary>
    public static CommandContext FromHotkey() => new(CommandSource.Hotkey);

    /// <summary>Context for the tray menu: it acts on the foreground window.</summary>
    public static CommandContext FromTray() => new(CommandSource.Tray);

    /// <summary>Context for the application's own UI, optionally aimed at a specific window.</summary>
    public static CommandContext FromUi(nint targetWindow = 0) => new(CommandSource.UserInterface, targetWindow);

    /// <summary>Context for a rule, which always knows the window it matched.</summary>
    public static CommandContext FromRule(nint targetWindow) => new(CommandSource.Rule, targetWindow);
}
