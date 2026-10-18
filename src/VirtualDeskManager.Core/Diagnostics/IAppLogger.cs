namespace VirtualDeskManager.Core.Diagnostics;

/// <summary>How important a log line is. Lines below the logger's minimum are not written.</summary>
public enum LogLevel
{
    /// <summary>Step-by-step detail, only useful while chasing a specific problem.</summary>
    Trace,
    /// <summary>Detail for diagnosing; off in normal use.</summary>
    Debug,
    /// <summary>Normal events worth keeping: something was done.</summary>
    Information,
    /// <summary>Something went wrong but the application carried on.</summary>
    Warning,
    /// <summary>An operation failed.</summary>
    Error,
    /// <summary>As a minimum level, turns logging off. Never used for a line.</summary>
    None
}

/// <summary>
/// Minimal structured logger (spec section 34). Deliberately dependency free: the application
/// must stay small and must not pull a logging framework just to write an event name plus
/// a handful of properties.
/// </summary>
public interface IAppLogger
{
    /// <summary>Lowest level that gets written. Can be changed while running.</summary>
    LogLevel MinimumLevel { get; set; }

    /// <summary>
    /// Whether a line at <paramref name="level"/> would be written, so callers can skip building
    /// expensive properties.
    /// </summary>
    bool IsEnabled(LogLevel level);

    /// <summary>Writes one structured line.</summary>
    /// <param name="level">How important the line is.</param>
    /// <param name="eventName">One of the names in <see cref="LogEvents"/>.</param>
    /// <param name="message">Optional free text.</param>
    /// <param name="exception">Optional exception, written in full after the line.</param>
    /// <param name="properties">Key/value pairs written as <c>Key=Value</c>.</param>
    void Log(
        LogLevel level,
        string eventName,
        string? message,
        Exception? exception,
        params (string Key, object? Value)[] properties);
}

/// <summary>Shortcuts for <see cref="IAppLogger.Log"/>, one per level.</summary>
public static class AppLoggerExtensions
{
    /// <summary>Writes a <see cref="LogLevel.Trace"/> line with no message.</summary>
    public static void Trace(this IAppLogger logger, string eventName, params (string Key, object? Value)[] properties) =>
        logger.Log(LogLevel.Trace, eventName, null, null, properties);

    /// <summary>Writes a <see cref="LogLevel.Debug"/> line with no message.</summary>
    public static void Debug(this IAppLogger logger, string eventName, params (string Key, object? Value)[] properties) =>
        logger.Log(LogLevel.Debug, eventName, null, null, properties);

    /// <summary>Writes a <see cref="LogLevel.Information"/> line with no message.</summary>
    public static void Info(this IAppLogger logger, string eventName, params (string Key, object? Value)[] properties) =>
        logger.Log(LogLevel.Information, eventName, null, null, properties);

    /// <summary>Writes a <see cref="LogLevel.Warning"/> line with no message.</summary>
    public static void Warn(this IAppLogger logger, string eventName, params (string Key, object? Value)[] properties) =>
        logger.Log(LogLevel.Warning, eventName, null, null, properties);

    /// <summary>Writes a <see cref="LogLevel.Warning"/> line with a free-text message.</summary>
    public static void Warn(this IAppLogger logger, string eventName, string message, params (string Key, object? Value)[] properties) =>
        logger.Log(LogLevel.Warning, eventName, message, null, properties);

    /// <summary>
    /// Writes a <see cref="LogLevel.Error"/> line for an exception; its message becomes the
    /// line's message and the full exception follows.
    /// </summary>
    public static void Error(this IAppLogger logger, string eventName, Exception? exception, params (string Key, object? Value)[] properties) =>
        logger.Log(LogLevel.Error, eventName, exception?.Message, exception, properties);

    /// <summary>Writes a <see cref="LogLevel.Error"/> line with a free-text message and no exception.</summary>
    public static void Error(this IAppLogger logger, string eventName, string message, params (string Key, object? Value)[] properties) =>
        logger.Log(LogLevel.Error, eventName, message, null, properties);
}
