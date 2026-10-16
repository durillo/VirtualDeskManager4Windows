using VirtualDeskManager.Core.Events;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>
/// Event based window observation. No polling (spec sections 11 and 37).
/// Handlers run on a worker thread, never inside the native callback.
/// </summary>
public interface IWindowEventService
{
    /// <summary>A window has been created. Windows of this application are not reported.</summary>
    event EventHandler<WindowDetectedEventArgs>? WindowCreated;
    /// <summary>A window has become visible.</summary>
    event EventHandler<WindowDetectedEventArgs>? WindowShown;
    /// <summary>Another window has come to the foreground.</summary>
    event EventHandler<WindowDetectedEventArgs>? ForegroundWindowChanged;
    /// <summary>A window has been destroyed. Its details may no longer be available.</summary>
    event EventHandler<WindowDetectedEventArgs>? WindowDestroyed;

    /// <summary>Whether the native hooks are installed.</summary>
    bool IsRunning { get; }

    /// <summary>Installs the native hooks and starts raising events.</summary>
    void Start();

    /// <summary>Removes the hooks. No more events are raised afterwards.</summary>
    void Stop();
}
