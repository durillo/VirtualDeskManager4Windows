using System.Runtime.InteropServices;
using System.Threading.Channels;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Events;

/// <summary>
/// Window observation through SetWinEventHook (spec section 11). No polling at all.
/// </summary>
/// <remarks>
/// The native callback only pushes the handle into a bounded channel: Microsoft warns explicitly
/// about re-entrancy inside WinEvent callbacks, so no Win32 call, no logging and no rule
/// evaluation happens there. A single background worker drains the channel, de-duplicates the
/// bursts Windows produces for one window and raises the public events.
/// </remarks>
public sealed class WinEventWindowEventService : IWindowEventService, IDisposable
{
    private const int ChannelCapacity = 2048;
    private const int MaxTrackedWindows = 1024;

    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(200);

    private readonly Win32MessageLoop _loop;
    private readonly IWindowManager _windows;
    private readonly IAppLogger _logger;
    private readonly User32.WinEventProcDelegate _callback;
    private readonly Channel<PendingEvent> _channel;
    private readonly Dictionary<(nint Handle, WindowEventKind Kind), DateTimeOffset> _lastSeen = new();
    private readonly object _gate = new();

    private nint _objectHook;
    private nint _foregroundHook;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private bool _disposed;

    /// <summary>Prepares the service. Nothing is hooked until <see cref="Start"/>.</summary>
    /// <param name="loop">Thread whose message loop owns the hooks.</param>
    /// <param name="windows">Used to filter out windows the rules should not see and to describe the rest.</param>
    /// <param name="logger">Optional diagnostic log.</param>
    public WinEventWindowEventService(Win32MessageLoop loop, IWindowManager windows, IAppLogger? logger = null)
    {
        _loop = loop;
        _windows = windows;
        _logger = logger ?? NullAppLogger.Instance;

        // Kept in a field: Windows calls this delegate for the whole lifetime of the hook.
        _callback = OnWinEvent;

        _channel = Channel.CreateBounded<PendingEvent>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>A manageable top level window was created.</summary>
    public event EventHandler<WindowDetectedEventArgs>? WindowCreated;
    /// <summary>A manageable top level window became visible.</summary>
    public event EventHandler<WindowDetectedEventArgs>? WindowShown;
    /// <summary>A manageable window took the foreground.</summary>
    public event EventHandler<WindowDetectedEventArgs>? ForegroundWindowChanged;
    /// <summary>A window was destroyed.</summary>
    /// <remarks>
    /// Raised for any top level window, not only manageable ones, and without
    /// <see cref="WindowDetectedEventArgs"/> details: by then there is nothing left to ask about.
    /// </remarks>
    public event EventHandler<WindowDetectedEventArgs>? WindowDestroyed;

    /// <summary>True while at least one of the hooks is installed.</summary>
    public bool IsRunning => _objectHook != 0 || _foregroundHook != 0;

    /// <summary>
    /// Installs the hooks on the message loop thread and starts the worker that raises the events.
    /// </summary>
    /// <remarks>Does nothing if already running. A failure is logged, not thrown.</remarks>
    public void Start()
    {
        if (_disposed || IsRunning)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _worker = Task.Run(() => ProcessAsync(_cancellation.Token));

        int lastError = 0;

        // The hooks must be created on the thread that owns the message loop, and they must be
        // released from that very same thread.
        _loop.Invoke(() =>
        {
            _objectHook = User32.SetWinEventHook(
                NativeConstants.EVENT_OBJECT_CREATE,
                NativeConstants.EVENT_OBJECT_SHOW,
                0,
                _callback,
                0,
                0,
                NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);

            _foregroundHook = User32.SetWinEventHook(
                NativeConstants.EVENT_SYSTEM_FOREGROUND,
                NativeConstants.EVENT_SYSTEM_FOREGROUND,
                0,
                _callback,
                0,
                0,
                NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);

            if (_objectHook == 0 || _foregroundHook == 0)
            {
                lastError = Marshal.GetLastWin32Error();
            }
        });

        if (!IsRunning)
        {
            _logger.Error(
                LogEvents.NativeApiError,
                "SetWinEventHook failed; automatic rules will not react to new windows.",
                ("Error", lastError));
            return;
        }

        _logger.Info(LogEvents.WindowEventServiceStarted);
    }

    /// <summary>Removes the hooks, stops the worker and forgets the de-duplication history.</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _loop.Invoke(() =>
            {
                if (_objectHook != 0)
                {
                    User32.UnhookWinEvent(_objectHook);
                    _objectHook = 0;
                }

                if (_foregroundHook != 0)
                {
                    User32.UnhookWinEvent(_foregroundHook);
                    _foregroundHook = 0;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Stop)));
            _objectHook = 0;
            _foregroundHook = 0;
        }

        try
        {
            _cancellation?.Cancel();
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Cancellation of the worker is expected.
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _worker = null;
        }

        lock (_gate)
        {
            _lastSeen.Clear();
        }

        _logger.Info(LogEvents.WindowEventServiceStopped);
    }

    /// <summary>
    /// Native callback. Must stay as small as possible: it runs inside the hooked message flow.
    /// </summary>
    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (hwnd == 0 || idObject != NativeConstants.OBJID_WINDOW || idChild != NativeConstants.CHILDID_SELF)
        {
            return;
        }

        WindowEventKind kind;

        switch (eventType)
        {
            case NativeConstants.EVENT_OBJECT_CREATE:
                kind = WindowEventKind.Created;
                break;
            case NativeConstants.EVENT_OBJECT_SHOW:
                kind = WindowEventKind.Shown;
                break;
            case NativeConstants.EVENT_OBJECT_DESTROY:
                kind = WindowEventKind.Destroyed;
                break;
            case NativeConstants.EVENT_SYSTEM_FOREGROUND:
                kind = WindowEventKind.ForegroundChanged;
                break;
            default:
                return;
        }

        _channel.Writer.TryWrite(new PendingEvent(hwnd, kind));
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out PendingEvent pending))
                {
                    Process(pending);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(ProcessAsync)));
        }
    }

    private void Process(PendingEvent pending)
    {
        if (IsDuplicate(pending))
        {
            return;
        }

        if (pending.Kind == WindowEventKind.Destroyed)
        {
            Forget(pending.Handle);
            Raise(WindowDestroyed, new WindowDetectedEventArgs(pending.Handle, pending.Kind, null));
            return;
        }

        // The window may already be gone: that is normal, not an error.
        if (!_windows.IsManageableWindow(pending.Handle))
        {
            return;
        }

        WindowInfo? info = _windows.GetWindowInfo(pending.Handle);

        if (info is null)
        {
            return;
        }

        _logger.Trace(
            LogEvents.WindowDetected,
            ("Kind", pending.Kind),
            ("Window", pending.Handle),
            ("Process", info.ProcessName),
            ("Title", info.Title));

        WindowDetectedEventArgs args = new(pending.Handle, pending.Kind, info);

        switch (pending.Kind)
        {
            case WindowEventKind.Created:
                Raise(WindowCreated, args);
                break;
            case WindowEventKind.Shown:
                Raise(WindowShown, args);
                break;
            case WindowEventKind.ForegroundChanged:
                Raise(ForegroundWindowChanged, args);
                break;
        }
    }

    private void Raise(EventHandler<WindowDetectedEventArgs>? handler, WindowDetectedEventArgs args)
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, args);
        }
        catch (Exception ex)
        {
            // One broken subscriber must not stop the whole event pipeline.
            _logger.Error(LogEvents.WindowDetected, ex, ("Window", args.Handle), ("Kind", args.Kind));
        }
    }

    private bool IsDuplicate(PendingEvent pending)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (nint Handle, WindowEventKind Kind) key = (pending.Handle, pending.Kind);

        lock (_gate)
        {
            if (_lastSeen.TryGetValue(key, out DateTimeOffset last) && now - last < DuplicateWindow)
            {
                return true;
            }

            if (_lastSeen.Count > MaxTrackedWindows)
            {
                List<(nint, WindowEventKind)> stale = _lastSeen
                    .Where(pair => now - pair.Value > TimeSpan.FromMinutes(1))
                    .Select(pair => pair.Key)
                    .ToList();

                foreach ((nint, WindowEventKind) entry in stale)
                {
                    _lastSeen.Remove(entry);
                }

                if (_lastSeen.Count > MaxTrackedWindows)
                {
                    _lastSeen.Clear();
                }
            }

            _lastSeen[key] = now;
            return false;
        }
    }

    private void Forget(nint handle)
    {
        lock (_gate)
        {
            foreach (WindowEventKind kind in Enum.GetValues<WindowEventKind>())
            {
                _lastSeen.Remove((handle, kind));
            }
        }
    }

    /// <summary>Stops the service for good. It cannot be started again afterwards.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _channel.Writer.TryComplete();
        _disposed = true;
    }

    private readonly record struct PendingEvent(nint Handle, WindowEventKind Kind);
}
