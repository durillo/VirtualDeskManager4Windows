namespace VirtualDeskManager.Core.Diagnostics;

/// <summary>No-op logger used by unit tests and as a safe default.</summary>
public sealed class NullAppLogger : IAppLogger
{
    /// <summary>Shared instance; it has no state worth having two of.</summary>
    public static NullAppLogger Instance { get; } = new();

    /// <summary>Always starts at <see cref="LogLevel.None"/>; changing it has no effect.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.None;

    /// <summary>Always false, so callers skip building properties.</summary>
    public bool IsEnabled(LogLevel level) => false;

    /// <summary>Discards the line.</summary>
    public void Log(
        LogLevel level,
        string eventName,
        string? message,
        Exception? exception,
        params (string Key, object? Value)[] properties)
    {
        // Intentionally empty.
    }
}
