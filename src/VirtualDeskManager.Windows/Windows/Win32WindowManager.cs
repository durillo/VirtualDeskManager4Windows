using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Zones;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Windows;

/// <summary>
/// Win32 implementation of <see cref="IWindowManager"/> (spec section 8).
/// Every failure over a foreign window is converted into an <see cref="OperationResult"/>:
/// a window that vanished or an elevated window must never take the application down
/// (spec sections 35 and 36).
/// </summary>
public sealed class Win32WindowManager : IWindowManager
{
    private readonly IAppLogger _logger;

    /// <summary>Creates the manager. It keeps no state of its own: every call asks Windows again.</summary>
    public Win32WindowManager(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <inheritdoc />
    public nint GetForegroundWindow() => User32.GetForegroundWindow();

    /// <inheritdoc />
    public bool IsWindow(nint hwnd) => hwnd != 0 && User32.IsWindow(hwnd);

    /// <inheritdoc />
    public uint GetWindowProcessId(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return 0;
        }

        User32.GetWindowThreadProcessId(hwnd, out uint processId);
        return processId;
    }

    /// <inheritdoc />
    public bool IsAlwaysOnTop(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return false;
        }

        long exStyle = User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);
        return (exStyle & NativeConstants.WS_EX_TOPMOST) != 0;
    }

    /// <summary>
    /// A window worth showing to the user and worth applying rules to: visible, with a title,
    /// and not a tool window. Cloaked windows are deliberately kept, because a window living on
    /// another virtual desktop is reported as cloaked.
    /// </summary>
    public bool IsManageableWindow(nint hwnd)
    {
        if (!IsWindow(hwnd) || !User32.IsWindowVisible(hwnd))
        {
            return false;
        }

        long exStyle = User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);

        if ((exStyle & NativeConstants.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        return User32.GetWindowTextLength(hwnd) > 0;
    }

    /// <inheritdoc />
    public WindowInfo? GetWindowInfo(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return null;
        }

        try
        {
            User32.GetWindowThreadProcessId(hwnd, out uint processId);

            string executablePath = Kernel32.GetProcessImagePath(processId);
            string processName = executablePath.Length > 0 ? Path.GetFileName(executablePath) : string.Empty;
            long exStyle = User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);

            WindowBounds bounds = User32.GetWindowRect(hwnd, out RECT rect)
                ? new WindowBounds(rect.Left, rect.Top, rect.Width, rect.Height)
                : WindowBounds.Empty;

            return new WindowInfo
            {
                Handle = hwnd,
                ProcessId = processId,
                ProcessName = processName,
                ExecutablePath = executablePath,
                Title = User32.GetWindowTitle(hwnd),
                ClassName = User32.GetWindowClassName(hwnd),
                IsVisible = User32.IsWindowVisible(hwnd),
                IsTopMost = (exStyle & NativeConstants.WS_EX_TOPMOST) != 0,
                IsMinimized = User32.IsIconic(hwnd),
                IsMaximized = User32.IsZoomed(hwnd),
                Bounds = bounds
            };
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(GetWindowInfo)), ("Window", hwnd));
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WindowInfo> GetTopLevelWindows()
    {
        List<nint> handles = new();

        try
        {
            // The callback only collects handles: no managed work inside the enumeration.
            User32.EnumWindows(
                (hwnd, _) =>
                {
                    handles.Add(hwnd);
                    return true;
                },
                0);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(GetTopLevelWindows)));
            return Array.Empty<WindowInfo>();
        }

        List<WindowInfo> windows = new(handles.Count);

        foreach (nint handle in handles)
        {
            if (!IsManageableWindow(handle))
            {
                continue;
            }

            WindowInfo? info = GetWindowInfo(handle);

            if (info is not null)
            {
                windows.Add(info);
            }
        }

        return windows;
    }

    /// <inheritdoc />
    public OperationResult SetAlwaysOnTop(nint hwnd, bool enabled)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        nint insertAfter = enabled ? NativeConstants.HWND_TOPMOST : NativeConstants.HWND_NOTOPMOST;

        bool ok = User32.SetWindowPos(
            hwnd,
            insertAfter,
            0,
            0,
            0,
            0,
            NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOACTIVATE);

        return ok ? OperationResult.Ok() : FromLastError(nameof(SetAlwaysOnTop), hwnd);
    }

    /// <inheritdoc />
    public OperationResult MoveWindow(nint hwnd, int x, int y, int width, int height)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        bool ok = User32.SetWindowPos(
            hwnd,
            NativeConstants.HWND_TOP,
            x,
            y,
            width,
            height,
            NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);

        return ok ? OperationResult.Ok() : FromLastError(nameof(MoveWindow), hwnd);
    }

    /// <inheritdoc />
    public OperationResult SetWindowState(nint hwnd, WindowVisualState state)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        int command = state switch
        {
            WindowVisualState.Maximized => NativeConstants.SW_MAXIMIZE,
            WindowVisualState.Minimized => NativeConstants.SW_MINIMIZE,
            _ => NativeConstants.SW_RESTORE
        };

        // ShowWindow returns the previous visibility, not a success flag, so the only way to
        // report a problem is to check the window again afterwards.
        User32.ShowWindow(hwnd, command);

        return IsWindow(hwnd) ? OperationResult.Ok() : OperationResult.InvalidWindow();
    }

    /// <inheritdoc />
    public OperationResult ActivateWindow(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        if (User32.IsIconic(hwnd))
        {
            User32.ShowWindow(hwnd, NativeConstants.SW_RESTORE);
        }

        return User32.SetForegroundWindow(hwnd)
            ? OperationResult.Ok()
            : OperationResult.Fail("Windows refused the foreground change (foreground lock).");
    }

    /// <remarks>
    /// Con <c>EnumDisplayMonitors</c> y no con WinForms: todo el Win32 de esta aplicación vive en
    /// este proyecto, y meter aquí una referencia a WinForms sólo para contar pantallas rompería
    /// eso. Además así el orden es el del sistema, que es el que hay que respetar.
    /// </remarks>
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        List<MonitorInfo> monitors = new();

        try
        {
            int number = 0;

            User32.EnumDisplayMonitors(
                0,
                0,
                (nint monitor, nint _, ref RECT _, nint _) =>
                {
                    MONITORINFO info = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };

                    if (User32.GetMonitorInfo(monitor, ref info))
                    {
                        number++;

                        monitors.Add(new MonitorInfo(
                            number,
                            new WindowBounds(
                                info.rcMonitor.Left,
                                info.rcMonitor.Top,
                                info.rcMonitor.Width,
                                info.rcMonitor.Height),
                            new WindowBounds(
                                info.rcWork.Left,
                                info.rcWork.Top,
                                info.rcWork.Width,
                                info.rcWork.Height),
                            (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0));
                    }

                    return true;
                },
                0);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(GetMonitors)));
        }

        return monitors;
    }

    /// <inheritdoc />
    public MonitorInfo? GetMonitorAtCursor()
    {
        try
        {
            if (!User32.GetCursorPos(out POINT cursor))
            {
                return null;
            }

            IReadOnlyList<MonitorInfo> monitors = GetMonitors();

            foreach (MonitorInfo monitor in monitors)
            {
                if (cursor.X >= monitor.Bounds.X
                    && cursor.X < monitor.Bounds.Right
                    && cursor.Y >= monitor.Bounds.Y
                    && cursor.Y < monitor.Bounds.Bottom)
                {
                    return monitor;
                }
            }

            // El cursor puede estar en un hueco entre monitores de distinto tamaño, o en una
            // pantalla que acaba de desconectarse. La principal es la respuesta menos sorprendente.
            return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(GetMonitorAtCursor)));
            return null;
        }
    }

    /// <remarks>
    /// El orden es el que da Windows, y el número de la regla es la posición en esa lista
    /// empezando por uno, que es lo mismo que enseña el desplegable del editor.
    ///
    /// Una ventana maximizada se restaura, se mueve y se vuelve a maximizar: si se moviera tal
    /// cual, Windows la dejaría maximizada en el monitor de origen y no se movería nada.
    /// </remarks>
    public OperationResult MoveWindowToMonitor(nint hwnd, int monitorNumber)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        try
        {
            IReadOnlyList<MonitorInfo> monitors = GetMonitors();

            if (monitorNumber < 1 || monitorNumber > monitors.Count)
            {
                return OperationResult.Fail(
                    $"There is no monitor {monitorNumber}; this machine has {monitors.Count}.");
            }

            WindowBounds target = monitors[monitorNumber - 1].WorkingArea;

            if (!User32.GetWindowRect(hwnd, out RECT rect))
            {
                return OperationResult.Fail("The window bounds could not be read.");
            }

            bool wasMaximized = User32.IsZoomed(hwnd);

            if (wasMaximized)
            {
                User32.ShowWindow(hwnd, NativeConstants.SW_RESTORE);
                User32.GetWindowRect(hwnd, out rect);
            }

            // El tamaño se conserva, pero nunca mayor que el monitor de destino: una ventana de
            // un panorámico no cabe en uno pequeño y quedaría medio fuera.
            int width = Math.Min(rect.Width, target.Width);
            int height = Math.Min(rect.Height, target.Height);

            int x = target.X + (target.Width - width) / 2;
            int y = target.Y + (target.Height - height) / 2;

            OperationResult moved = MoveWindow(hwnd, x, y, width, height);

            if (wasMaximized)
            {
                User32.ShowWindow(hwnd, NativeConstants.SW_MAXIMIZE);
            }

            if (moved.IsSuccess)
            {
                _logger.Info(
                    LogEvents.DesktopOperation,
                    ("Operation", nameof(MoveWindowToMonitor)),
                    ("Window", hwnd),
                    ("Monitor", monitorNumber),
                    ("Remaximized", wasMaximized));
            }

            return moved;
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(MoveWindowToMonitor)), ("Window", hwnd));
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <remarks>
    /// Posted rather than sent, and WM_CLOSE rather than anything harsher. Posting does not wait
    /// for the application to answer, so a program busy putting up "save your changes?" cannot
    /// block the caller; and WM_CLOSE is the message its own close button sends, so the
    /// application keeps every chance to object or to save first.
    /// </remarks>
    public OperationResult CloseWindow(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        try
        {
            if (User32.PostMessage(hwnd, NativeConstants.WM_CLOSE, 0, 0))
            {
                _logger.Info(LogEvents.DesktopOperation, ("Operation", nameof(CloseWindow)), ("Window", hwnd));
                return OperationResult.Ok();
            }

            int error = Marshal.GetLastWin32Error();

            return error == NativeConstants.ERROR_ACCESS_DENIED
                ? OperationResult.Denied()
                : OperationResult.Fail($"WM_CLOSE could not be posted (error {error}).");
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(CloseWindow)), ("Window", hwnd));
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public OperationResult PlaceOnTop(nint hwnd, int x, int y, int width, int height)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        // No SWP_NOZORDER here on purpose: the point is to climb back to the top of the topmost
        // band every time, in case the window underneath has just joined it.
        bool ok = User32.SetWindowPos(
            hwnd,
            NativeConstants.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            NativeConstants.SWP_NOACTIVATE);

        return ok ? OperationResult.Ok() : FromLastError(nameof(PlaceOnTop), hwnd);
    }

    /// <inheritdoc />
    public OperationResult SetNonActivating(nint hwnd, bool enabled)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        long exStyle = User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);

        // The tool window style stays either way: the button never belongs in Alt+Tab.
        long updated = enabled
            ? exStyle | NativeConstants.WS_EX_NOACTIVATE | NativeConstants.WS_EX_TOOLWINDOW
            : (exStyle & ~NativeConstants.WS_EX_NOACTIVATE) | NativeConstants.WS_EX_TOOLWINDOW;

        if (exStyle == updated)
        {
            return OperationResult.Ok();
        }

        // SetWindowLongPtr returns 0 both on failure and when the previous value was 0, so the
        // last error has to be cleared first to tell them apart.
        System.Runtime.InteropServices.Marshal.SetLastSystemError(0);
        User32.SetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE, (nint)updated);

        int error = Marshal.GetLastWin32Error();
        return error == 0 ? OperationResult.Ok() : FromLastError(nameof(SetNonActivating), hwnd);
    }

    /// <inheritdoc />
    public int? GetCornerPreference(nint hwnd) =>
        IsWindow(hwnd) ? Dwmapi.TryGetCornerPreference(hwnd) : null;

    /// <inheritdoc />
    public OperationResult SetCornerPreference(nint hwnd, int preference)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        return Dwmapi.TrySetCornerPreference(hwnd, preference)
            ? OperationResult.Ok()
            : OperationResult.Fail("El gestor de ventanas del escritorio no ha aceptado el cambio.");
    }

    /// <inheritdoc />
    public OperationResult SetSquareCorners(nint hwnd, bool enabled) =>
        SetCornerPreference(hwnd, enabled ? Dwmapi.DWMWCP_DONOTROUND : Dwmapi.DWMWCP_DEFAULT);

    /// <inheritdoc />
    public OperationResult SetBorderVisible(nint hwnd, bool visible)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        return Dwmapi.TrySetBorderVisible(hwnd, visible)
            ? OperationResult.Ok()
            : OperationResult.Fail("El gestor de ventanas del escritorio no ha aceptado el cambio.");
    }

    /// <summary>
    /// Mueve la ventana de modo que su borde <b>visible</b> caiga en el rectangulo pedido.
    /// </summary>
    /// <remarks>
    /// Windows deja siete pixeles transparentes a cada lado y abajo de casi cualquier ventana, y
    /// MoveWindow los cuenta. Sin esta correccion, dos ventanas puestas en zonas contiguas quedan
    /// separadas por catorce pixeles que nadie ha pedido.
    /// </remarks>
    public OperationResult MoveWindowVisible(nint hwnd, int x, int y, int width, int height)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        WindowBounds target = new(x, y, width, height);
        WindowBounds? visible = Dwmapi.TryGetVisibleBounds(hwnd);

        if (visible is WindowBounds seen && User32.GetWindowRect(hwnd, out RECT frame))
        {
            target = WindowFrame.Adjust(
                target,
                new WindowBounds(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top),
                seen);
        }

        return MoveWindow(hwnd, target.X, target.Y, target.Width, target.Height);
    }

    /// <summary>
    /// Hace que el raton atraviese la ventana como si no estuviera.
    /// </summary>
    /// <remarks>
    /// Para la superposicion que ensena las zonas mientras se arrastra. Windows esta en mitad de
    /// un arrastre y le manda los movimientos del raton a la ventana que se arrastra; si la
    /// superposicion se los quedara, el arrastre se cortaria en seco en cuanto apareciera.
    /// </remarks>
    public OperationResult SetClickThrough(nint hwnd, bool enabled)
    {
        if (!IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        long exStyle = User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE);

        long updated = enabled
            ? exStyle | NativeConstants.WS_EX_TRANSPARENT
            : exStyle & ~NativeConstants.WS_EX_TRANSPARENT;

        if (exStyle == updated)
        {
            return OperationResult.Ok();
        }

        System.Runtime.InteropServices.Marshal.SetLastSystemError(0);
        User32.SetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE, (nint)updated);

        int error = Marshal.GetLastWin32Error();
        return error == 0 ? OperationResult.Ok() : FromLastError(nameof(SetClickThrough), hwnd);
    }
    private OperationResult FromLastError(string operation, nint hwnd)
    {
        int error = Marshal.GetLastWin32Error();

        _logger.Warn(
            LogEvents.NativeApiError,
            ("Operation", operation),
            ("Window", hwnd),
            ("Error", error));

        return error switch
        {
            NativeConstants.ERROR_ACCESS_DENIED => OperationResult.Denied(
                "Windows denied access to the window; it probably belongs to an elevated process."),
            NativeConstants.ERROR_INVALID_WINDOW_HANDLE => OperationResult.InvalidWindow(),
            0 => OperationResult.Fail($"{operation} failed."),
            _ => OperationResult.Fail($"{operation} failed (Win32 error {error}).")
        };
    }
}
