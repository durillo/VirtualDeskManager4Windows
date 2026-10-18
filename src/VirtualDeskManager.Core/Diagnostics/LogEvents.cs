namespace VirtualDeskManager.Core.Diagnostics;

/// <summary>Canonical event names (spec section 34). Never build them inline.</summary>
public static class LogEvents
{
    /// <summary>The application finished starting up.</summary>
    public const string ApplicationStarted = "ApplicationStarted";
    /// <summary>The application is shutting down.</summary>
    public const string ApplicationStopped = "ApplicationStopped";

    /// <summary>A configuration file was read.</summary>
    public const string ConfigurationLoaded = "ConfigurationLoaded";
    /// <summary>A configuration file could not be read or parsed; defaults are used instead.</summary>
    public const string ConfigurationInvalid = "ConfigurationInvalid";
    /// <summary>A configuration file was written.</summary>
    public const string ConfigurationSaved = "ConfigurationSaved";

    /// <summary>A configuration backup was written to history, the cloud folder or an export.</summary>
    public const string BackupSaved = "BackupSaved";
    /// <summary>The configuration was replaced from a backup.</summary>
    public const string BackupRestored = "BackupRestored";
    /// <summary>A backup, export or restore failed; the property <c>Stage</c> says which.</summary>
    public const string BackupFailed = "BackupFailed";

    /// <summary>A global hotkey was registered with Windows.</summary>
    public const string HotkeyRegistered = "HotkeyRegistered";
    /// <summary>A hotkey could not be registered: invalid, or already taken by someone else.</summary>
    public const string HotkeyRegistrationFailed = "HotkeyRegistrationFailed";
    /// <summary>The user pressed a registered hotkey.</summary>
    public const string HotkeyTriggered = "HotkeyTriggered";
    /// <summary>A hotkey was released back to Windows.</summary>
    public const string HotkeyUnregistered = "HotkeyUnregistered";

    /// <summary>A command ran successfully.</summary>
    public const string CommandDispatched = "CommandDispatched";
    /// <summary>A command was invalid, failed or threw.</summary>
    public const string CommandFailed = "CommandFailed";

    /// <summary>The window event service saw a window appear, show or take the foreground.</summary>
    public const string WindowDetected = "WindowDetected";
    /// <summary>The window event hooks were installed.</summary>
    public const string WindowEventServiceStarted = "WindowEventServiceStarted";
    /// <summary>The window event hooks were removed.</summary>
    public const string WindowEventServiceStopped = "WindowEventServiceStopped";

    /// <summary>A window matched at least one rule.</summary>
    public const string RuleMatched = "RuleMatched";
    /// <summary>A rule's actions were applied to a window.</summary>
    public const string RuleExecuted = "RuleExecuted";
    /// <summary>Applying a rule threw.</summary>
    public const string RuleFailed = "RuleFailed";
    /// <summary>A rule matched but was deliberately not applied.</summary>
    public const string RuleSkipped = "RuleSkipped";
    /// <summary>A rule cannot be evaluated, for example because of a broken or too slow regex.</summary>
    public const string RuleInvalid = "RuleInvalid";

    /// <summary>A desktop or window operation was carried out (switch, move, pin, create, rename…).</summary>
    public const string DesktopOperation = "DesktopOperation";
    /// <summary>A desktop or window operation failed.</summary>
    public const string DesktopOperationFailed = "DesktopOperationFailed";
    /// <summary>The virtual desktop backend in use was chosen.</summary>
    public const string DesktopBackendSelected = "DesktopBackendSelected";
    /// <summary>A virtual desktop backend could not be used on this Windows build.</summary>
    public const string DesktopBackendUnavailable = "DesktopBackendUnavailable";

    /// <summary>A Win32 or COM call failed; the property <c>Where</c> names the caller.</summary>
    public const string NativeApiError = "NativeApiError";
}
