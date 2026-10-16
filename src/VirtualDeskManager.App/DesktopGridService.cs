using System;
using System.Windows.Threading;
using VirtualDeskManager.App.Views;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App;

/// <summary>
/// Owns the overview window and keeps it alive between openings (spec section 17).
/// </summary>
/// <remarks>
/// The window is built once and then only shown and hidden, never created again. Creating a full
/// screen WPF window costs enough to be felt as a delay on the keystroke that asked for it,
/// whereas showing one that already exists does not, and the user asked for this view to be up
/// immediately.
///
/// Every call crosses a thread boundary on the way in: shortcuts arrive on the Win32 message
/// loop thread, and a WPF window may only be touched from the thread that made it.
/// </remarks>
public sealed class DesktopGridService : IDesktopGridView, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<DesktopGridWindow> _create;
    private readonly IAppLogger _logger;

    private DesktopGridWindow? _window;
    private bool _disposed;

    /// <summary>Takes what it needs to build the window later; nothing is created yet.</summary>
    /// <param name="dispatcher">The interface thread, the only one allowed to touch the window.</param>
    /// <param name="create">Builds the window the first time it is needed.</param>
    /// <param name="logger">Where failures go; none means they are dropped.</param>
    public DesktopGridService(Dispatcher dispatcher, Func<DesktopGridWindow> create, IAppLogger? logger = null)
    {
        _dispatcher = dispatcher;
        _create = create;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <inheritdoc />
    public bool IsOpen => !_disposed && _dispatcher.Invoke(() => _window is { IsOpen: true });

    /// <summary>
    /// Builds the window and its handle now, while the application is starting, so that the
    /// first press of the shortcut has nothing left to do but show it.
    /// </summary>
    public void Prewarm() => _dispatcher.Invoke(() => Ensure().Prewarm());

    /// <inheritdoc />
    public OperationResult Show() => Run(window => window.ShowGrid());

    /// <inheritdoc />
    public OperationResult Hide() => Run(window => window.HideGrid());

    private OperationResult Run(Action<DesktopGridWindow> action)
    {
        if (_disposed)
        {
            return OperationResult.Fail("The application is shutting down.");
        }

        try
        {
            _dispatcher.Invoke(() => action(Ensure()));
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Where", nameof(DesktopGridService)));
            return OperationResult.Fail(ex.Message);
        }
    }

    private DesktopGridWindow Ensure() => _window ??= _create();

    /// <summary>Closes the window for good. Calls after this answer with a failure.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _dispatcher.Invoke(() =>
        {
            _window?.Close();
            _window = null;
        });
    }
}
