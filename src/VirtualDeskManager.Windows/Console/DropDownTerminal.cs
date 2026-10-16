using System.Diagnostics;
using System.Text;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Console;

/// <summary>
/// A real shell, in a window we borrow and drive: stripped of its frame, made translucent and
/// moved about so it can slide in and out of the top of the screen.
/// </summary>
/// <remarks>
/// Borrowed rather than hosted, and that was measured rather than chosen. On Windows 11 the
/// console of a command line program is not drawn by that program: the shell gets an invisible
/// <c>PseudoConsoleWindow</c> and the picture belongs to the default terminal application, which
/// on this build is Windows Terminal, in its own process, often one that was already running.
/// So there is no console window of ours to re-parent, and asking the launched process for its
/// window finds nothing. Forcing <c>conhost.exe</c> did not bring one back either.
///
/// What does work is treating it as what it is: somebody else's window that we manage. This
/// application is a window manager, so that is familiar ground.
///
/// Two obligations come with borrowing. The frame has to go back before we let go of it, because
/// leaving another program's window with no title bar is leaving the user with a window they
/// cannot move. And the window has to be one we brought into being, never one the user was
/// already working in, which is why the terminal is launched into a window of its own.
/// </remarks>
public sealed class DropDownTerminal : IDisposable
{
    /// <summary>Processes that may be the one actually drawing a console.</summary>
    /// <remarks>
    /// The launched process is often not the one that owns the window, so the window is found by
    /// watching for one that appears, and this is what keeps that from picking up whatever else
    /// happened to pop up at the same moment. The first attempt, with no filter at all, grabbed
    /// this application's own floating caption button.
    /// </remarks>
    private static readonly string[] TerminalProcesses =
    {
        "WindowsTerminal",
        "OpenConsole",
        "conhost",
        "cmd",
        "powershell",
        "pwsh"
    };

    private const long Frame =
        NativeConstants.WS_CAPTION
        | NativeConstants.WS_THICKFRAME
        | NativeConstants.WS_SYSMENU
        | NativeConstants.WS_MINIMIZEBOX
        | NativeConstants.WS_MAXIMIZEBOX
        | NativeConstants.WS_BORDER;

    private readonly IAppLogger _logger;

    private Process? _process;
    private long _originalStyle;
    private long _originalExStyle;
    private bool _restyled;

    /// <summary>Creates the controller. No shell is started until <see cref="Start"/>.</summary>
    public DropDownTerminal(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <summary>The borrowed window, or 0 when there is no terminal running.</summary>
    public nint Handle { get; private set; }

    /// <summary>True while there is a live window to slide around.</summary>
    public bool IsAlive => Handle != 0 && User32.IsWindow(Handle);

    /// <summary>
    /// Starts the shell and takes over its window, or reports why it could not.
    /// </summary>
    public OperationResult Start(DropDownConsoleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (IsAlive)
        {
            return OperationResult.Ok();
        }

        (string file, string arguments) = CommandFor(settings);

        if (file.Length == 0)
        {
            return OperationResult.Fail("No shell is configured for the drop down console.");
        }

        HashSet<nint> before = VisibleWindows();
        DateTime startedAt = DateTime.Now.AddSeconds(-1);

        try
        {
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = arguments,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Where", nameof(Start)), ("Shell", file));
            return OperationResult.Fail($"{file} could not be started: {ex.Message}");
        }

        nint window = WaitForNewTerminalWindow(before, TimeSpan.FromSeconds(8));

        if (window == 0)
        {
            // Se cierra lo que se haya levantado. Sin esto, cada intento fallido dejaba un
            // intérprete y su host de consola vivos para siempre: en una mañana se acumularon
            // cuarenta procesos en el equipo del usuario.
            KillStrays(startedAt);
            Kill();

            return OperationResult.Fail($"{file} started but no terminal window of its own appeared.");
        }

        Handle = window;
        Restyle(settings);

        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", nameof(Start)),
            ("What", "DropDownConsole"),
            ("Shell", file),
            ("Window", "0x" + window.ToString("X")));

        return OperationResult.Ok();
    }

    /// <summary>
    /// The command line for the configured shell.
    /// </summary>
    /// <remarks>
    /// Everything goes through <c>wt.exe</c> with a named window when Windows Terminal is
    /// present. The name is what guarantees a window of our own: without it, Windows Terminal may
    /// well open the shell as a new tab inside whatever terminal the user already had open, and
    /// we would then strip the frame off their working window and drag it to the top of the
    /// screen. With it, the same window comes back every time, which is also exactly what is
    /// wanted for reopening.
    /// </remarks>
    private static (string File, string Arguments) CommandFor(DropDownConsoleSettings settings)
    {
        string shell = settings.Shell switch
        {
            ConsoleShell.PowerShell => "powershell.exe",
            ConsoleShell.PowerShellCore => "pwsh.exe",
            ConsoleShell.WindowsTerminal => "",
            ConsoleShell.Custom => settings.CustomCommand.Trim(),
            _ => "cmd.exe"
        };

        if (settings.Shell == ConsoleShell.Custom && shell.Length == 0)
        {
            return ("", "");
        }

        if (HasWindowsTerminal())
        {
            // -w new y no -w _vdesk, que es lo que se puso primero y estaba mal.
            //
            // Un nombre fijo hace que Windows Terminal reutilice la ventana que ya tenga con ese
            // nombre. La primera vez se creaba y todo iba bien; a partir de ahí no aparecía
            // ninguna ventana nueva, y como la ventana se busca justamente viendo cuál aparece,
            // dejaba de encontrarse para siempre. Cada intento se saldaba con ocho segundos de
            // espera y un intérprete huérfano.
            //
            // "new" es un nombre reservado que obliga a ventana nueva, que es lo que hace falta
            // aquí: una ventana propia, recién nacida y por tanto detectable. Sigue sin
            // secuestrar la del usuario, que era la razón de usar -w en primer lugar.
            return ("wt.exe", shell.Length == 0 ? "-w new" : $"-w new {shell}");
        }

        return settings.Shell == ConsoleShell.WindowsTerminal
            ? ("cmd.exe", "")
            : (shell, "");
    }

    private static bool HasWindowsTerminal()
    {
        try
        {
            string? path = Environment.GetEnvironmentVariable("PATH");

            if (path is null)
            {
                return false;
            }

            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (directory.Length > 0 && File.Exists(Path.Combine(directory, "wt.exe")))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // A malformed PATH is not worth failing over; fall back to launching the shell.
        }

        return false;
    }

    private static HashSet<nint> VisibleWindows()
    {
        HashSet<nint> windows = new();

        User32.EnumWindows(
            (hwnd, _) =>
            {
                if (User32.IsWindowVisible(hwnd))
                {
                    windows.Add(hwnd);
                }

                return true;
            },
            0);

        return windows;
    }

    private nint WaitForNewTerminalWindow(HashSet<nint> before, TimeSpan timeout)
    {
        DateTime until = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < until)
        {
            foreach (nint hwnd in VisibleWindows())
            {
                if (before.Contains(hwnd) || User32.GetWindowTextLength(hwnd) == 0)
                {
                    continue;
                }

                User32.GetWindowThreadProcessId(hwnd, out uint processId);

                if (IsTerminalProcess(processId))
                {
                    return hwnd;
                }
            }

            Thread.Sleep(100);
        }

        return 0;
    }

    private bool IsTerminalProcess(uint processId)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(Kernel32.GetProcessImagePath(processId));

            foreach (string candidate in TerminalProcesses)
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(LogEvents.NativeApiError, ("Where", nameof(IsTerminalProcess)), ("Error", ex.Message));
        }

        return false;
    }

    /// <summary>Turns the terminal window into a bare, translucent panel.</summary>
    private void Restyle(DropDownConsoleSettings settings)
    {
        _originalStyle = User32.GetWindowLongPtr(Handle, NativeConstants.GWL_STYLE);
        _originalExStyle = User32.GetWindowLongPtr(Handle, NativeConstants.GWL_EXSTYLE);

        User32.SetWindowLongPtr(Handle, NativeConstants.GWL_STYLE, (nint)(_originalStyle & ~Frame));

        // Layered for the opacity, tool window to keep it out of Alt+Tab and the taskbar: it is
        // summoned by a key and dismissed by one, so a second way in would only be a way to
        // strand it somewhere with no frame to drag it back by.
        long exStyle = _originalExStyle | NativeConstants.WS_EX_LAYERED | NativeConstants.WS_EX_TOOLWINDOW;

        User32.SetWindowLongPtr(Handle, NativeConstants.GWL_EXSTYLE, (nint)exStyle);

        _restyled = true;

        ApplyOpacity(settings);
    }

    /// <summary>Applies the configured translucency to the borrowed window.</summary>
    /// <remarks>Also called on its own when the setting changes while the console is open.</remarks>
    public void ApplyOpacity(DropDownConsoleSettings settings)
    {
        if (!IsAlive)
        {
            return;
        }

        if (!User32.SetLayeredWindowAttributes(Handle, 0, settings.Alpha, NativeConstants.LWA_ALPHA))
        {
            _logger.Debug(LogEvents.NativeApiError, ("Where", nameof(ApplyOpacity)));
        }
    }

    /// <summary>Moves the panel, without activating it or disturbing what is in front.</summary>
    public void Place(LayoutRect rect, bool activate)
    {
        if (!IsAlive)
        {
            return;
        }

        uint flags = NativeConstants.SWP_SHOWWINDOW | (activate ? 0 : NativeConstants.SWP_NOACTIVATE);

        User32.SetWindowPos(
            Handle,
            NativeConstants.HWND_TOPMOST,
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height,
            flags);
    }

    /// <summary>Hides the panel. The shell keeps running, ready for the next time.</summary>
    public void Hide()
    {
        if (IsAlive)
        {
            User32.ShowWindow(Handle, NativeConstants.SW_HIDE);
        }
    }

    /// <summary>Brings the borrowed window to the foreground so it takes the keyboard.</summary>
    public void Focus()
    {
        if (IsAlive)
        {
            User32.SetForegroundWindow(Handle);
        }
    }

    /// <summary>Class name of the borrowed window, for the log.</summary>
    public string ClassName
    {
        get
        {
            if (!IsAlive)
            {
                return "";
            }

            StringBuilder name = new(128);
            return User32.GetClassName(Handle, name, name.Capacity) > 0 ? name.ToString() : "";
        }
    }

    /// <summary>
    /// Gives the window its frame back, which has to happen whether we are closing the shell or
    /// merely letting go of it.
    /// </summary>
    private void Restore()
    {
        if (!_restyled || !IsAlive)
        {
            return;
        }

        try
        {
            User32.SetWindowLongPtr(Handle, NativeConstants.GWL_STYLE, (nint)_originalStyle);
            User32.SetWindowLongPtr(Handle, NativeConstants.GWL_EXSTYLE, (nint)_originalExStyle);
            User32.ShowWindow(Handle, NativeConstants.SW_SHOWNORMAL);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Restore)));
        }

        _restyled = false;
    }

    /// <summary>
    /// Cierra los intérpretes que se hayan quedado sin ventana desde que empezamos.
    /// </summary>
    /// <remarks>
    /// Matar el proceso que lanzamos no basta: <c>wt.exe</c> delega en el Windows Terminal que ya
    /// esté vivo y se va enseguida, así que matarlo no se lleva por delante ni el intérprete ni
    /// el host de consola que han quedado colgando. Se buscan por hora de arranque, que es lo
    /// único que los relaciona con nosotros.
    /// </remarks>
    private void KillStrays(DateTime since)
    {
        foreach (string name in new[] { "OpenConsole", "conhost" })
        {
            Process[] candidates;

            try
            {
                candidates = Process.GetProcessesByName(name);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (Process candidate in candidates)
            {
                try
                {
                    // Sólo los nacidos desde que lo intentamos y sin ventana que enseñar: los que
                    // tienen ventana son de alguien y no se tocan.
                    if (candidate.StartTime >= since && candidate.MainWindowHandle == 0)
                    {
                        candidate.Kill(entireProcessTree: true);

                        _logger.Debug(
                            LogEvents.DesktopOperation,
                            ("Operation", "DropDownConsoleStrayClosed"),
                            ("Process", name),
                            ("Pid", candidate.Id));
                    }
                }
                catch (Exception)
                {
                    // Un proceso que ya se ha ido, o que no nos deja: ninguno es un problema.
                }
                finally
                {
                    candidate.Dispose();
                }
            }
        }
    }

    private void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(LogEvents.NativeApiError, ("Where", nameof(Kill)), ("Error", ex.Message));
        }

        _process?.Dispose();
        _process = null;
        Handle = 0;
    }

    /// <summary>
    /// Gives the window its frame back and then ends the shell it was launched for.
    /// </summary>
    public void Dispose()
    {
        Restore();
        Kill();
    }
}
