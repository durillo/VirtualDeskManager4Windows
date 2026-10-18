using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Messaging;

/// <summary>One message received by the window of <see cref="Win32MessageLoop"/>, as the window procedure got it.</summary>
/// <param name="Hwnd">Window the message was sent to.</param>
/// <param name="Message">Message identifier, such as <c>WM_HOTKEY</c>.</param>
/// <param name="WParam">First message parameter; its meaning depends on the message.</param>
/// <param name="LParam">Second message parameter; its meaning depends on the message.</param>
public readonly record struct Win32Message(nint Hwnd, uint Message, nint WParam, nint LParam);

/// <summary>
/// The one and only Win32 message pump of the application (spec section 38).
/// </summary>
/// <remarks>
/// <c>RegisterHotKey</c> and <c>SetWinEventHook</c> both need a thread with a message loop, and
/// <c>UnregisterHotKey</c> / <c>UnhookWinEvent</c> must run on the very thread that created them.
/// A dedicated STA thread owning a message only window gives that guarantee without ever
/// blocking the WPF UI thread, and it keeps working while the settings window is closed.
/// </remarks>
public sealed class Win32MessageLoop : IDisposable
{
    /// <summary>Posted by <see cref="Invoke"/> to drain the work queue on the loop thread.</summary>
    public const uint WM_INVOKE = NativeConstants.WM_APP + 1;

    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(5);

    private readonly IAppLogger _logger;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ConcurrentQueue<WorkItem> _queue = new();
    private readonly User32.WndProcDelegate _wndProc;
    private readonly string _className = "VirtualDeskManager_MessageWindow_" + Guid.NewGuid().ToString("N");

    private nint _hwnd;
    private nint _instance;
    private Exception? _startupError;
    private volatile bool _disposed;

    /// <summary>
    /// Starts the loop thread and waits until its window exists.
    /// </summary>
    /// <remarks>If the window cannot be created, the error is rethrown here, on the caller's thread.</remarks>
    public Win32MessageLoop(IAppLogger? logger = null)
    {
        _logger = logger ?? NullAppLogger.Instance;

        // The delegate must be kept alive for as long as the window class exists,
        // otherwise the GC collects it and Windows calls into freed memory.
        _wndProc = WndProc;

        _thread = new Thread(ThreadMain)
        {
            Name = "VirtualDeskManager.MessageLoop",
            IsBackground = true
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();

        if (_startupError is not null)
        {
            ExceptionDispatchInfo.Capture(_startupError).Throw();
        }
    }

    /// <summary>Handle of the message only window. Hotkeys are registered against it.</summary>
    public nint Handle => _hwnd;

    /// <summary>True while the window exists and the loop has not been disposed.</summary>
    public bool IsRunning => !_disposed && _hwnd != 0;

    /// <summary>Raised on the loop thread for every message the window receives.</summary>
    public event EventHandler<Win32Message>? MessageReceived;

    /// <summary>Runs <paramref name="action"/> on the loop thread and waits for it.</summary>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
        {
            action();
            return;
        }

        if (_disposed || _hwnd == 0)
        {
            throw new InvalidOperationException("The Win32 message loop is not running.");
        }

        WorkItem item = new(action);
        _queue.Enqueue(item);

        if (!User32.PostMessage(_hwnd, WM_INVOKE, 0, 0))
        {
            item.Dispose();
            throw new InvalidOperationException("The work item could not be posted to the message loop.");
        }

        if (!item.Completed.Wait(InvokeTimeout))
        {
            throw new TimeoutException("The Win32 message loop did not answer in time.");
        }

        Exception? error = item.Error;
        item.Dispose();

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>Runs <paramref name="function"/> on the loop thread, waits for it and returns its result.</summary>
    public T Invoke<T>(Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        T result = default!;

        // The body must be a statement lambda: `() => result = function()` is also a valid
        // Func<T>, so overload resolution would bind it back to this very method and recurse
        // until the stack overflows.
        Invoke(() => { result = function(); });

        return result;
    }

    private void ThreadMain()
    {
        try
        {
            _instance = Kernel32.GetModuleHandle(null);

            WNDCLASSEX windowClass = new()
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = _instance,
                lpszClassName = _className
            };

            if (User32.RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException(
                    $"RegisterClassEx failed (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
            }

            _hwnd = User32.CreateWindowEx(
                0,
                _className,
                "VirtualDeskManager",
                0,
                0,
                0,
                0,
                0,
                NativeConstants.HWND_MESSAGE,
                0,
                _instance,
                0);

            if (_hwnd == 0)
            {
                throw new InvalidOperationException(
                    $"CreateWindowEx failed (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
            }
        }
        catch (Exception ex)
        {
            _startupError = ex;
            _ready.Set();
            return;
        }

        _ready.Set();

        while (true)
        {
            int result = User32.GetMessage(out MSG message, 0, 0, 0);

            if (result == 0 || result == -1)
            {
                break;
            }

            User32.TranslateMessage(ref message);
            User32.DispatchMessage(ref message);
        }

        Cleanup();
    }

    private void Cleanup()
    {
        try
        {
            if (_hwnd != 0)
            {
                User32.DestroyWindow(_hwnd);
                _hwnd = 0;
            }

            User32.UnregisterClass(_className, _instance);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Cleanup)));
        }

        DrainQueue();
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_INVOKE)
        {
            DrainQueue();
            return 0;
        }

        if (msg == NativeConstants.WM_DESTROY)
        {
            _hwnd = 0;
            User32.PostQuitMessage(0);
            return 0;
        }

        try
        {
            MessageReceived?.Invoke(this, new Win32Message(hwnd, msg, wParam, lParam));
        }
        catch (Exception ex)
        {
            // A subscriber must never be able to break the pump.
            _logger.Error(LogEvents.NativeApiError, ex, ("Message", msg));
        }

        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void DrainQueue()
    {
        while (_queue.TryDequeue(out WorkItem? item))
        {
            item.Run();
        }
    }

    /// <summary>Closes the window, which ends the loop, and waits up to three seconds for the thread to stop.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        nint hwnd = _hwnd;

        if (hwnd != 0)
        {
            User32.PostMessage(hwnd, NativeConstants.WM_CLOSE, 0, 0);
        }

        if (!_thread.Join(TimeSpan.FromSeconds(3)))
        {
            _logger.Warn(LogEvents.NativeApiError, "The message loop thread did not stop within three seconds.");
        }

        _ready.Dispose();
    }

    private sealed class WorkItem : IDisposable
    {
        private readonly Action _action;

        public WorkItem(Action action) => _action = action;

        public ManualResetEventSlim Completed { get; } = new(false);

        public Exception? Error { get; private set; }

        public void Run()
        {
            try
            {
                _action();
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                Completed.Set();
            }
        }

        public void Dispose() => Completed.Dispose();
    }
}
