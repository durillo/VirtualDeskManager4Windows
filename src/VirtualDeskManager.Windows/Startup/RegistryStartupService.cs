using Microsoft.Win32;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;

namespace VirtualDeskManager.Windows.Startup;

/// <summary>
/// Start with Windows through the per user Run key (spec section 33).
/// </summary>
/// <remarks>
/// Section 24 asks not to keep functional configuration in the registry; the Run key is the
/// exception it allows, because it is the mechanism Windows itself defines. No administrator
/// rights are needed: the value lives under HKEY_CURRENT_USER.
/// </remarks>
public sealed class RegistryStartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VirtualDeskManager";

    private readonly IAppLogger _logger;

    /// <summary>Creates the service. It keeps no state: the registry is read on every call.</summary>
    public RegistryStartupService(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <summary>False only when the path of the running executable is unknown, so there is nothing to register.</summary>
    public bool IsSupported => !string.IsNullOrEmpty(Environment.ProcessPath);

    /// <summary>True when the Run key has a non-empty value for this application.</summary>
    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(IsEnabled)));
            return false;
        }
    }

    /// <summary>Writes the Run value: the current executable, quoted, with <c>--minimized</c>.</summary>
    /// <remarks>Errors are logged, not thrown. The work is synchronous; the task is already complete.</remarks>
    public Task EnableAsync()
    {
        try
        {
            string? path = Environment.ProcessPath;

            if (string.IsNullOrEmpty(path))
            {
                return Task.CompletedTask;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, $"\"{path}\" --minimized", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(EnableAsync)));
        }

        return Task.CompletedTask;
    }

    /// <summary>Removes the Run value, if there is one.</summary>
    /// <remarks>Errors are logged, not thrown. The work is synchronous; the task is already complete.</remarks>
    public Task DisableAsync()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(DisableAsync)));
        }

        return Task.CompletedTask;
    }
}
