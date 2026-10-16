namespace VirtualDeskManager.Core.Models;

/// <summary>Normalized outcome of any operation performed against Windows (spec section 19).</summary>
public enum OperationStatus
{
    /// <summary>It was done.</summary>
    Success,
    /// <summary>It was tried and did not work.</summary>
    Failed,
    /// <summary>The active backend or the current settings cannot do it; nothing was tried.</summary>
    Unsupported,
    /// <summary>Windows refused, usually because the window belongs to an elevated or protected process.</summary>
    AccessDenied,
    /// <summary>The window no longer exists, or there was no window to act on.</summary>
    InvalidWindow,
    /// <summary>The request makes no sense in the current state, such as moving left from the first desktop.</summary>
    Conflict
}

/// <summary>
/// Result of an operation. Internal APIs never return a bare <c>bool</c> so that
/// "not supported by this backend" is distinguishable from "it failed" (spec sections 19 and 35).
/// </summary>
public sealed record OperationResult(OperationStatus Status, string? Error = null)
{
    /// <summary>True only for <see cref="OperationStatus.Success"/>.</summary>
    public bool IsSuccess => Status == OperationStatus.Success;

    /// <summary>A success, with no message.</summary>
    public static OperationResult Ok() => new(OperationStatus.Success);

    /// <summary>A plain failure. The message is optional.</summary>
    public static OperationResult Fail(string? error = null) =>
        new(OperationStatus.Failed, error);

    /// <summary>Not available here. Without a message, a generic one about the backend is used.</summary>
    public static OperationResult NotSupported(string? error = null) =>
        new(OperationStatus.Unsupported, error ?? "Not supported by the active backend.");

    /// <summary>Refused by Windows. Without a message, one about elevated or protected windows is used.</summary>
    public static OperationResult Denied(string? error = null) =>
        new(OperationStatus.AccessDenied, error ?? "Access denied by Windows (elevated or protected window).");

    /// <summary>The window is gone or there is none. Without a message, a generic one is used.</summary>
    public static OperationResult InvalidWindow(string? error = null) =>
        new(OperationStatus.InvalidWindow, error ?? "The window handle is not valid any more.");

    /// <summary>Not possible in the current state. The message is optional.</summary>
    public static OperationResult Conflicted(string? error = null) =>
        new(OperationStatus.Conflict, error);

    /// <summary>Status and message for the log, e.g. <c>Failed: reason</c>.</summary>
    public override string ToString() => Error is null ? Status.ToString() : $"{Status}: {Error}";
}
