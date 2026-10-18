using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Overlay;

/// <summary>
/// Follows the foreground window and reports where its title bar button belongs.
/// </summary>
/// <remarks>
/// Windows has no supported way to add a button to the caption of another application: doing it
/// for real means injecting a DLL into that process, which section 36 forbids. So the button is a
/// window of our own that floats over the caption, and this tracker keeps it glued to the right
/// place using the same WinEvent mechanism as the rest of the application, never polling.
///
/// EVENT_OBJECT_LOCATIONCHANGE fires continuously while a window is dragged, so the anchor is
/// only published when it actually changed.
/// </remarks>
public sealed class ActiveWindowTracker : IActiveWindowTracker, IDisposable
{
    private readonly Win32MessageLoop _loop;
    private readonly IWindowManager _windows;
    private readonly IAppLogger _logger;
    private readonly User32.WinEventProcDelegate _callback;
    private readonly object _gate = new();

    /// <summary>
    /// When the application starts, the desktop has not settled: our own process may hold the
    /// foreground for an instant while WPF creates its windows, and the window underneath may not
    /// have finished drawing its caption. A first look would then find nothing and, with no
    /// further window event, the button would stay hidden until the user happened to change
    /// windows. So the first seconds are re-checked a few times.
    /// </summary>
    private static readonly int[] SettleDelays = { 250, 750, 1500, 3000 };

    private nint _locationHook;
    private nint _foregroundHook;
    private TitleBarAnchor? _lastAnchor;
    private bool _hasPublished;
    private bool _disposed;
    private readonly List<Timer> _settleTimers = new();

    private readonly uint _ownProcessId;

    /// <summary>Prepares the tracker. Nothing is hooked until <see cref="Start"/>.</summary>
    /// <param name="loop">Thread whose message loop owns the hooks.</param>
    /// <param name="windows">Used to read the geometry of the window in front.</param>
    /// <param name="ownProcessId">This application's process, whose windows never get a button.</param>
    /// <param name="logger">Optional diagnostic log.</param>
    public ActiveWindowTracker(
        Win32MessageLoop loop,
        IWindowManager windows,
        uint ownProcessId,
        IAppLogger? logger = null)
    {
        _loop = loop;
        _windows = windows;
        _ownProcessId = ownProcessId;
        _logger = logger ?? NullAppLogger.Instance;

        // Kept alive in a field for as long as the hooks exist.
        _callback = OnWinEvent;
    }

    /// <inheritdoc />
    public bool IsRunning => _locationHook != 0 || _foregroundHook != 0;

    /// <inheritdoc />
    public int ExtraOffset { get; set; }

    /// <inheritdoc />
    public int SizePercent { get; set; } = 100;

    /// <summary>
    /// Last window the button was attached to, which is the one the user was working on.
    /// </summary>
    /// <remarks>
    /// The tray menu needs it: clicking a tray icon hands the foreground to the shell, so by the
    /// time a menu entry runs, asking Windows which window is in front gives the wrong answer.
    /// </remarks>
    public nint LastTrackedWindow
    {
        get
        {
            lock (_gate)
            {
                return _lastAnchor?.Handle ?? 0;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<TitleBarAnchorEventArgs>? AnchorChanged;

    /// <inheritdoc />
    public void Start()
    {
        if (_disposed || IsRunning)
        {
            return;
        }

        _loop.Invoke(() =>
        {
            // Movement, resize, minimize and destroy of any window.
            _locationHook = User32.SetWinEventHook(
                NativeConstants.EVENT_SYSTEM_MOVESIZESTART,
                NativeConstants.EVENT_OBJECT_LOCATIONCHANGE,
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
        });

        if (!IsRunning)
        {
            _logger.Warn("TitleBarTrackerUnavailable", "The window could not be tracked; the title bar button stays hidden.");
            return;
        }

        _logger.Info("TitleBarTrackerStarted");
        Refresh();
        ScheduleSettleRefreshes();
    }

    /// <summary>
    /// Looks again a few times over the first seconds, so the button lands on the window that was
    /// already open when the application started instead of waiting for the user to switch.
    /// </summary>
    private void ScheduleSettleRefreshes()
    {
        lock (_gate)
        {
            foreach (int delay in SettleDelays)
            {
                Timer timer = new(
                    _ =>
                    {
                        if (!_disposed && IsRunning)
                        {
                            Refresh();
                        }
                    },
                    null,
                    delay,
                    Timeout.Infinite);

                _settleTimers.Add(timer);
            }
        }
    }

    private void CancelSettleRefreshes()
    {
        lock (_gate)
        {
            foreach (Timer timer in _settleTimers)
            {
                timer.Dispose();
            }

            _settleTimers.Clear();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        CancelSettleRefreshes();

        if (!IsRunning)
        {
            return;
        }

        try
        {
            _loop.Invoke(() =>
            {
                if (_locationHook != 0)
                {
                    User32.UnhookWinEvent(_locationHook);
                    _locationHook = 0;
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
            _locationHook = 0;
            _foregroundHook = 0;
        }

        Publish(null);
        _logger.Info("TitleBarTrackerStopped");
    }

    /// <summary>
    /// Always publishes, even if the anchor did not change: a subscriber that arrives after the
    /// tracker has started needs its first position, and the de-duplication would swallow it.
    /// </summary>
    public void Refresh() => Publish(ComputeForForegroundWindow(), force: true);

    /// <summary>Native callback: it only decides whether a recalculation is worth it.</summary>
    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        // A change in the stacking order has to be looked at before anything else is filtered.
        // Measured on Windows 11, it arrives on behalf of a different window and with
        // OBJID_CLIENT rather than OBJID_WINDOW, so the usual filters would throw it away. And it
        // matters: a window that has just been set to always on top joins the same band as the
        // button and ends up over it.
        if (eventType == NativeConstants.EVENT_OBJECT_REORDER)
        {
            Publish(ComputeForForegroundWindow());
            return;
        }

        if (idObject != NativeConstants.OBJID_WINDOW || idChild != NativeConstants.CHILDID_SELF)
        {
            return;
        }

        // Only the window in front carries the button, so anything else can be ignored outright.
        if (hwnd != 0 && hwnd != User32.GetForegroundWindow())
        {
            return;
        }

        Publish(ComputeForForegroundWindow());
    }

    private TitleBarAnchor? ComputeForForegroundWindow()
    {
        try
        {
            nint hwnd = _windows.GetForegroundWindow();

            if (hwnd == 0 || !User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd))
            {
                _logger.Trace("TitleBarAnchorSkipped", ("Window", hwnd), ("Reason", "not a visible window"));
                return null;
            }

            // Our own settings window, and above all the button itself, must not get a button.
            if (_windows.GetWindowProcessId(hwnd) == _ownProcessId)
            {
                _logger.Trace("TitleBarAnchorSkipped", ("Window", hwnd), ("Reason", "our own window"));
                return null;
            }

            if (!TryGetTitleBar(hwnd, out WindowBounds titleBar))
            {
                _logger.Trace("TitleBarAnchorSkipped", ("Window", hwnd), ("Reason", "no visible caption"));
                return null;
            }

            // Where the caption is really free, in order of trust:
            //   1. ask the window itself with WM_NCHITTEST, which also covers the buttons an
            //      application draws on its own (Visual Studio Code and its layout buttons);
            //   2. ask the desktop manager, which only knows the system buttons;
            //   3. fall back to an estimate based on the height of the caption.
            int required = TitleBarLayout.ComputeRequiredFreeWidth(titleBar.Height, SizePercent);

            int? captionButtons =
                CaptionProbe.MeasureReservedRight(hwnd, titleBar, required)
                ?? Dwmapi.TryGetCaptionButtonsWidth(hwnd);

            return TitleBarLayout.Compute(
                hwnd,
                titleBar,
                _windows.IsAlwaysOnTop(hwnd),
                ExtraOffset,
                captionButtons,
                SizePercent);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(ComputeForForegroundWindow)));
            return null;
        }
    }

    /// <summary>
    /// GetTitleBarInfo answers for windows of other processes, so no injection is needed to know
    /// where the caption of a foreign window is.
    /// </summary>
    private static bool TryGetTitleBar(nint hwnd, out WindowBounds titleBar)
    {
        titleBar = WindowBounds.Empty;

        TITLEBARINFO info = new()
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<TITLEBARINFO>(),
            rgstate = new uint[6]
        };

        if (!User32.GetTitleBarInfo(hwnd, ref info))
        {
            return false;
        }

        // A window whose caption is not visible (full screen, custom chrome) gets no button.
        if ((info.rgstate[0] & NativeConstants.STATE_SYSTEM_INVISIBLE) != 0)
        {
            return false;
        }

        RECT rect = info.rcTitleBar;
        titleBar = new WindowBounds(rect.Left, rect.Top, rect.Width, rect.Height);
        return !titleBar.IsEmpty;
    }

    private void Publish(TitleBarAnchor? anchor, bool force = false)
    {
        lock (_gate)
        {
            // A window that is always on top shares the band with the button and can climb over
            // it at any moment, so in that case the position is re-asserted even if nothing about
            // the anchor changed. It is a single SetWindowPos on a window of our own.
            bool mustReassert = anchor?.IsTopMost == true;

            // Dragging a window fires this hundreds of times a second; only real changes get through.
            if (!force && !mustReassert && _hasPublished && Nullable.Equals(_lastAnchor, anchor))
            {
                return;
            }

            _lastAnchor = anchor;
            _hasPublished = true;
        }

        try
        {
            _logger.Debug(
                "TitleBarAnchorPublished",
                ("Window", anchor?.Handle ?? 0),
                ("Shown", anchor is not null),
                ("Forced", force));

            AnchorChanged?.Invoke(this, new TitleBarAnchorEventArgs(anchor));
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Publish)));
        }
    }

    /// <summary>Stops tracking for good. The tracker cannot be started again afterwards.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}
