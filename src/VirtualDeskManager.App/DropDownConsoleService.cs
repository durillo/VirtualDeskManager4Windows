using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.App.Overlay;
using VirtualDeskManager.Windows.Console;
using VirtualDeskManager.Windows.Events;

namespace VirtualDeskManager.App;

/// <summary>
/// Drives the drop down console: rolls it in and out of the top of the screen and keeps the shell
/// alive in between.
/// </summary>
/// <remarks>
/// The shell is started once, the first time the shortcut is pressed, and from then on it is only
/// moved. That is the whole trick behind a console like Quake's feeling instant: it is never
/// launched, only uncovered, and it still has the history and whatever was left running.
///
/// The slide is driven by the clock rather than by a frame count, so the movement takes the same
/// time whether the machine can manage sixty frames a second or fifteen: a dropped frame makes
/// the console jump further, never take longer.
/// </remarks>
public sealed class DropDownConsoleService : IDropDownConsole, IDisposable
{
    /// <summary>About sixty frames a second, which is as smooth as moving a window gets.</summary>
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// How long after opening the console refuses to be dismissed for losing the focus.
    /// </summary>
    /// <remarks>
    /// Taking the foreground from another process is not instantaneous and not entirely ours to
    /// decide, so the moment the console appears there may still be a foreground change in
    /// flight, naming whatever the user was on before. Without this the console would roll back
    /// up in the same breath as it came down, look as though the shortcut did nothing, and leave
    /// nothing behind to explain why.
    /// </remarks>
    private static readonly TimeSpan FocusGrace = TimeSpan.FromMilliseconds(400);

    private readonly Dispatcher _dispatcher;
    private readonly Func<DropDownConsoleSettings> _settings;
    private readonly WinEventWindowEventService _windowEvents;

    /// <summary>Para saber en qué pantalla está el ratón cuando hay más de una.</summary>
    private readonly IWindowManager _windows;

    private readonly IAppLogger _logger;
    private readonly DropDownTerminal _terminal;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    /// <summary>La pestaña de la chincheta; se crea la primera vez que baja la consola.</summary>
    private ConsolePinWindow? _pin;

    private LayoutRect _screen;
    private bool _opening;
    private bool _disposed;
    private DateTime _openedAtUtc;

    /// <summary>1 mientras se está levantando el terminal, para no levantar dos.</summary>
    private int _starting;

    /// <summary>
    /// Prepara el temporizador de la animación y se engancha al primer plano. El terminal no se
    /// lanza hasta la primera vez que se pide la consola.
    /// </summary>
    /// <param name="dispatcher">El hilo de la interfaz, donde corre la animación.</param>
    /// <param name="settings">Los ajustes de la consola; se leen en cada uso, así un cambio vale al momento.</param>
    /// <param name="windowEvents">Avisa de los cambios de primer plano, para recogerla al perder el foco.</param>
    /// <param name="windows">Para saber en qué pantalla está el ratón.</param>
    /// <param name="logger">Dónde apuntar lo que pase.</param>
    public DropDownConsoleService(
        Dispatcher dispatcher,
        Func<DropDownConsoleSettings> settings,
        WinEventWindowEventService windowEvents,
        IWindowManager windows,
        IAppLogger? logger = null)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _windowEvents = windowEvents;
        _windows = windows;
        _logger = logger ?? NullAppLogger.Instance;
        _terminal = new DropDownTerminal(_logger);

        _timer = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = Frame };
        _timer.Tick += OnTick;

        _windowEvents.ForegroundWindowChanged += OnForegroundChanged;
    }

    /// <summary>True while the console is down or on its way down.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Fijada: no se recoge al pasar a otra ventana. Dura lo que dure la sesión; al volver a
    /// arrancar la aplicación la consola vuelve a estar suelta.
    /// </summary>
    public bool IsPinned { get; private set; }

    /// <inheritdoc />
    public OperationResult TogglePin() => OnDispatcher(() =>
    {
        IsPinned = !IsPinned;
        _pin?.SetPinned(IsPinned);

        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", IsPinned ? "Pin" : "Unpin"),
            ("What", "DropDownConsole"));

        return OperationResult.Ok();
    });

    /// <summary>
    /// Arranca la consola si no estaba, y la despliega.
    /// </summary>
    /// <remarks>
    /// El arranque va a un hilo aparte porque puede tardar segundos: hay que lanzar el terminal y
    /// esperar a que aparezca su ventana. Hacerlo en el hilo de la interfaz, que es lo que hacía
    /// antes, congelaba la aplicación entera mientras tanto, y con la ventana sin aparecer eran
    /// ocho segundos de bloqueo por pulsación. Tres pulsaciones seguidas dejaban el programa
    /// muerto veinticuatro segundos, que es exactamente lo que el usuario describió como que se
    /// queda pillado.
    /// </remarks>
    public OperationResult Show()
    {
        if (_disposed)
        {
            return OperationResult.Fail("The application is shutting down.");
        }

        DropDownConsoleSettings settings = Current();

        if (!settings.Enabled)
        {
            return OperationResult.NotSupported("The drop down console is switched off in settings.");
        }

        // Si ya hay consola viva, no hay nada lento que hacer: se despliega y punto.
        if (_dispatcher.Invoke(() => _terminal.IsAlive))
        {
            return Reveal();
        }

        // Una sola puesta en marcha a la vez. Sin esto, aporrear el atajo lanza un terminal por
        // pulsación, y los que no lleguen a tiempo quedan huérfanos.
        if (Interlocked.CompareExchange(ref _starting, 1, 0) != 0)
        {
            return OperationResult.Ok();
        }

        _ = Task.Run(() =>
        {
            OperationResult started;

            try
            {
                started = _terminal.Start(settings);
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.CommandFailed, ex, ("What", "DropDownConsole"));
                started = OperationResult.Fail(ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _starting, 0);
            }

            if (!started.IsSuccess)
            {
                _logger.Warn(
                    LogEvents.CommandFailed,
                    started.Error ?? "",
                    ("What", "DropDownConsole"),
                    ("Shell", settings.Shell));

                return;
            }

            _dispatcher.BeginInvoke(new Action(() => Reveal()));
        });

        return OperationResult.Ok();
    }

    /// <summary>Despliega una consola que ya está en marcha. Rápido y en el hilo de interfaz.</summary>
    private OperationResult Reveal() => OnDispatcher(() =>
    {
        DropDownConsoleSettings settings = Current();

        if (!_terminal.IsAlive)
        {
            return OperationResult.Fail("The console is not running.");
        }

        _screen = Screen(settings);

        // Placed out of sight before it is shown at all, or the first frame appears in the middle
        // of the screen and then jumps to the top edge.
        if (!IsOpen)
        {
            LayoutRect hidden = DropDownConsoleLayout.At(_screen, settings, 0);
            _terminal.Place(hidden, activate: false);
            PlacePin(hidden);
        }

        _terminal.ApplyOpacity(settings);
        _terminal.Focus();

        IsOpen = true;
        _openedAtUtc = DateTime.UtcNow;
        Animate(opening: true);

        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", nameof(Show)),
            ("What", "DropDownConsole"),
            ("Shell", settings.Shell),
            ("Class", _terminal.ClassName),
            ("Screen", $"{_screen.Width}x{_screen.Height}"));

        return OperationResult.Ok();
    });

    /// <inheritdoc />
    public OperationResult Hide() => OnDispatcher(() =>
    {
        if (!IsOpen)
        {
            return OperationResult.Ok();
        }

        IsOpen = false;
        Animate(opening: false);

        return OperationResult.Ok();
    });

    /// <inheritdoc />
    public OperationResult Toggle() => IsOpen ? Hide() : Show();

    private void Animate(bool opening)
    {
        _opening = opening;
        _clock.Restart();

        DropDownConsoleSettings settings = Current();

        if (settings.AnimationMilliseconds <= 0)
        {
            Finish(settings);
            return;
        }

        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        DropDownConsoleSettings settings = Current();

        if (!_terminal.IsAlive)
        {
            // The user typed exit. There is nothing left to slide.
            Stop();
            IsOpen = false;
            _pin?.HideTab();
            return;
        }

        double progress = DropDownConsoleLayout.Progress(_clock.Elapsed, settings, _opening);
        LayoutRect rect = DropDownConsoleLayout.At(_screen, settings, progress);

        _terminal.Place(rect, activate: false);
        PlacePin(rect);

        bool done = _opening ? progress >= 1 : progress <= 0;

        if (done)
        {
            Finish(settings);
        }
    }

    private void Finish(DropDownConsoleSettings settings)
    {
        Stop();

        if (_opening)
        {
            LayoutRect down = DropDownConsoleLayout.At(_screen, settings, 1);
            _terminal.Place(down, activate: true);
            _terminal.Focus();
            PlacePin(down);
            return;
        }

        // Hidden rather than parked above the screen: a window sitting off the top edge still
        // takes the foreground when Windows feels like it, and still shows up to anything
        // enumerating windows, this application's own rules included.
        _terminal.Hide();
        _pin?.HideTab();
    }

    /// <summary>Lleva la pestaña de la chincheta con la consola, creándola si hace falta.</summary>
    private void PlacePin(LayoutRect console)
    {
        if (_pin is null)
        {
            _pin = new ConsolePinWindow(_windows);
            _pin.SetPinned(IsPinned);
            _pin.Clicked += (_, _) => TogglePin();
        }

        _pin.PlaceAt(DropDownConsoleLayout.PinTab(console, _screen, _pin.SizePixels));
    }

    private void Stop()
    {
        _timer.Stop();
        _clock.Stop();
    }

    /// <summary>
    /// Rolls the console up as soon as the user goes back to whatever they were doing.
    /// </summary>
    /// <remarks>
    /// Through the foreground hook and not through a window event of our own, because the window
    /// belongs to the terminal's process: there is nothing of ours to be told it lost the focus.
    /// </remarks>
    private void OnForegroundChanged(object? sender, WindowDetectedEventArgs e)
    {
        if (_disposed || !IsOpen)
        {
            return;
        }

        // e.Handle y no e.Window.Handle: el detalle de la ventana puede no haberse podido leer
        // (una ventana elevada, o una que ya se ha ido), y entonces Window llega nulo.
        nint foreground = e.Handle;

        if (foreground == 0)
        {
            return;
        }

        if (foreground == _terminal.Handle)
        {
            // Al activarse, la consola sube al frente de las ventanas siempre encima; la pestaña
            // vuelve a ponerse por encima de ella por si alguna vez la tapa.
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsOpen && !_timer.IsEnabled && _terminal.IsAlive)
                {
                    PlacePin(DropDownConsoleLayout.At(_screen, Current(), 1));
                }
            }));

            return;
        }

        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsOpen)
            {
                return;
            }

            // Se ha escrito «exit»: ya no hay consola, y la pestaña no puede quedarse colgando
            // de nada.
            if (!_terminal.IsAlive)
            {
                Stop();
                IsOpen = false;
                _pin?.HideTab();
                return;
            }

            if (IsPinned || !Current().HideWhenFocusLost)
            {
                return;
            }

            if (DateTime.UtcNow - _openedAtUtc < FocusGrace)
            {
                _logger.Debug(
                    LogEvents.DesktopOperation,
                    ("Operation", "DropDownConsoleFocusIgnored"),
                    ("Reason", "still settling"));

                return;
            }

            _logger.Debug(LogEvents.DesktopOperation, ("Operation", "DropDownConsoleFocusLost"));
            Hide();
        }));
    }

    private DropDownConsoleSettings Current()
    {
        DropDownConsoleSettings settings = _settings() ?? new DropDownConsoleSettings();
        settings.Normalise();
        return settings;
    }

    /// <summary>
    /// La pantalla en la que baja la consola, en píxeles reales.
    /// </summary>
    /// <remarks>
    /// Por el gestor de ventanas y no por <c>SystemParameters</c>: éste contesta en unidades
    /// independientes del dispositivo, y la ventana que se mueve es de otro proceso y se coloca
    /// en píxeles físicos. En una pantalla con escala no son el mismo número.
    ///
    /// Con la opción de seguir al ratón, la pantalla es aquella donde esté el cursor; si no se
    /// puede averiguar, la principal, que es la respuesta menos sorprendente.
    /// </remarks>
    private LayoutRect Screen(DropDownConsoleSettings settings)
    {
        MonitorInfo? monitor = settings.FollowMouse ? _windows.GetMonitorAtCursor() : null;

        monitor ??= _windows.GetMonitors().FirstOrDefault(m => m.IsPrimary)
                    ?? _windows.GetMonitors().FirstOrDefault();

        if (monitor is null)
        {
            // Sin monitores que consultar no queda nada sensato que hacer; la pantalla primaria
            // de WPF al menos da un rectángulo.
            return new LayoutRect(
                0,
                0,
                (int)SystemParameters.PrimaryScreenWidth,
                (int)SystemParameters.PrimaryScreenHeight);
        }

        return new LayoutRect(
            monitor.Bounds.X,
            monitor.Bounds.Y,
            monitor.Bounds.Width,
            monitor.Bounds.Height);
    }

    private OperationResult OnDispatcher(Func<OperationResult> action)
    {
        if (_disposed)
        {
            return OperationResult.Fail("The application is shutting down.");
        }

        try
        {
            return _dispatcher.Invoke(action);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.CommandFailed, ex, ("What", "DropDownConsole"));
            return OperationResult.Fail(ex.Message);
        }
    }

    /// <summary>Para la animación, devuelve el marco al terminal y lo cierra.</summary>
    /// <remarks>
    /// Cerrarlo es a propósito: un terminal vivo sin barra de título y aparcado fuera de la pantalla
    /// sería una ventana que el usuario no puede ver ni alcanzar.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _windowEvents.ForegroundWindowChanged -= OnForegroundChanged;

        _dispatcher.Invoke(() =>
        {
            Stop();

            _pin?.Close();
            _pin = null;

            // The frame goes back on and the shell is closed: leaving a terminal alive with no
            // title bar, parked off the top of the screen, would be leaving the user a window
            // they can neither see nor reach.
            _terminal.Dispose();
        });
    }
}
