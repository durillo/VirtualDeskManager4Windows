using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VirtualDeskManager.App.Overlay;
using VirtualDeskManager.App.Tray;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.App.Views;
using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using VirtualDeskManager.Core.Updates;

namespace VirtualDeskManager.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\VirtualDeskManager.SingleInstance";

    private Mutex? _singleInstance;
    private AppHost? _host;
    private TrayService? _tray;
    private MainViewModel? _viewModel;
    private TitleBarButtonWindow? _titleBarButton;
    private RememberWindowService? _remember;
    private FollowMoveToast? _followToast;
    private DesktopGridService? _grid;
    private UpdateService? _updates;
    private DropDownConsoleService? _console;
    private DesktopIndicatorService? _indicator;

    /// <summary>
    /// Arranca Virtual Desk Manager: comprueba que no haya otra copia, pone en marcha <see cref="AppHost"/> y monta
    /// la bandeja, las ventanas y los servicios de la interfaz.
    /// </summary>
    /// <remarks>
    /// Si ya hay otra copia, avisa y se sale. La ventana principal sólo se queda oculta al arrancar
    /// minimizado (ajuste o <c>--minimized</c>) si hay icono en la bandeja; sin él no habría forma
    /// de volver a ella.
    /// </remarks>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Capturas de la ventana principal con datos de ejemplo: antes que nada, sin tocar la copia en
        // marcha (ver Views/Enlace/Escaparate.cs).
        if (e.Args is ["--escaparate", { } carpetaEscaparate])
        {
            Views.Enlace.Escaparate.Run(carpetaEscaparate);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, SingleInstanceMutexName, out bool createdNew);

        if (!createdNew)
        {
            // Abrir otra vez el programa con él ya en marcha es pedir su ventana: se le pide a la
            // copia que está en marcha y esta se va sin decir nada. Sólo si aquella es una versión
            // que no entiende la señal, el aviso de siempre.
            if (AskRunningCopyToShowSettings())
            {
                Shutdown();
                return;
            }

            // The settings have not been read yet, so this first message follows the Windows language.
            Loc.Instance.Use(LocalizationService.AutomaticCode);

            MessageBox.Show(
                Loc.Get("App.AlreadyRunning"),
                AppVersion.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _host = new AppHost();
            _host.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Loc.Get("App.StartupFailed") + Environment.NewLine + ex.Message,
                AppVersion.ProductName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
            return;
        }

        ListenForQuitSignal();
        ListenForShowSettingsSignal();

        _remember = new RememberWindowService(_host);

        // Las versiones con enlace entre equipos dejaban entradas en «Enviar a» del Explorador. El
        // enlace es ahora MiRed: se quitan, fuera del hilo de la interfaz.
        AppHost anfitrion = _host;
        _ = Task.Run(() => VirtualDeskManager.Windows.Shell.SendToShortcuts.RemoveAll(anfitrion.Logger));

        CreateDesktopGrid();
        CreateDropDownConsole();

        _host.RuleApplication.WindowMovedToAnotherDesktop += OnRuleMovedWindow;
        _viewModel = new MainViewModel(_host, Dispatcher);
        _viewModel.RulesPausedChanged += (_, paused) =>
        {
            if (_tray is not null)
            {
                _tray.RulesPaused = paused;
                _tray.UpdateIcon(paused);
            }
        };

        if (_host.Settings.ShowTrayIcon)
        {
            _tray = new TrayService();
            _tray.OpenSettingsRequested += (_, _) => ShowMainWindow();
            _tray.ReloadRequested += (_, _) => _host.ReloadConfiguration();
            _tray.ExitRequested += (_, _) => ExitApplication();
            _tray.PauseRulesChanged += (_, paused) => OnPauseRules(paused);
            _tray.CommandRequested += (_, type) => RunTrayCommand(type);
            _tray.AlwaysOpenHereRequested += (_, _) => RememberActiveWindow();
            _tray.UpdateRequested += (_, _) => _ = _updates?.InstallAsync();
            _tray.VdeskRequested += (_, _) => ShowVdesk();
        }

        // La actualización por internet: busca sola, avisa en la bandeja y pregunta.
        _updates = new UpdateService(
            () => _host.Settings,
            () => _host.Configuration.SaveSettings(),
            Dispatcher,
            (titulo, texto) => _tray?.Notify(titulo, texto.Length > 0 ? texto : titulo),
            ExitApplication,
            () => AutoUpdatePolicy.CanRestartNow(VirtualDeskManager.Windows.Native.UserIdle.Elapsed()),
            _host.Logger);
        _updates.Changed += (_, _) => _tray?.ShowUpdate(_updates.Available?.Version);
        _viewModel.Updates = new UpdatesViewModel(_updates, _host);

        CreateDesktopIndicator();

        CreateTitleBarButton();

        bool startMinimized = _host.Settings.StartMinimized
                              || e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        // Without a tray icon there would be no way back to the application.
        if (!startMinimized || !_host.Settings.ShowTrayIcon)
        {
            ShowVdesk();
        }
    }

    /// <summary>
    /// Builds the overview of every desktop and warms it up.
    /// </summary>
    /// <remarks>
    /// The window is created and given its handle while the application is starting, which is
    /// the only moment nobody is waiting. The shortcut then has nothing left to do but show it,
    /// which is what the whole view was asked to feel like.
    /// </remarks>
    private void CreateDesktopGrid()
    {
        if (_host is null)
        {
            return;
        }

        AppHost host = _host;

        _grid = new DesktopGridService(
            Dispatcher,
            () => new DesktopGridWindow(
                host.VirtualDesktops,
                host.WindowManager,
                (uint)Environment.ProcessId,
                host.Logger,
                new Overlay.WindowMenu(
                    host.VirtualDesktops,
                    host.WindowManager,
                    (command, target) => host.ExecuteAsync(command, CommandContext.FromUi(target))),
                new AccionesEscritorios(host)),
            host.Logger);

        host.AttachDesktopGrid(_grid);
        _grid.Prewarm();
    }

    /// <summary>
    /// Builds the console that drops down from the top of the screen.
    /// </summary>
    /// <remarks>
    /// Nothing is launched here. The shell starts the first time the shortcut is pressed and then
    /// stays alive, which is what makes every later press instant.
    /// </remarks>
    private void CreateDropDownConsole()
    {
        if (_host is null)
        {
            return;
        }

        AppHost host = _host;

        _console = new DropDownConsoleService(
            Dispatcher,
            () => host.Settings.DropDownConsole,
            host.WindowEvents,
            host.WindowManager,
            host.Logger,
            () => host.Configuration.SaveSettings());

        host.AttachDropDownConsole(_console);
    }

    /// <summary>
    /// Pone el escritorio actual en el icono de la bandeja.
    /// </summary>
    /// <remarks>
    /// Sin bandeja no hay dónde ponerlo, así que si el usuario la ha apagado esto no existe.
    /// </remarks>
    private void CreateDesktopIndicator()
    {
        if (_host is null || _tray is null)
        {
            return;
        }

        AppHost host = _host;

        _indicator = new DesktopIndicatorService(
            Dispatcher,
            host.VirtualDesktops,
            host.WindowEvents,
            _tray,
            () => host.Settings.ShowDesktopInTray,
            host.Logger);

        _viewModel!.DesktopIndicatorChanged += (_, _) => _indicator.Refresh();

        _indicator.Start();
    }

    /// <summary>
    /// Creates the floating caption button and keeps it glued to the active window.
    /// The anchor arrives on the Win32 message loop thread, so it is marshalled to the UI thread.
    /// </summary>
    private void CreateTitleBarButton()
    {
        if (_host is null)
        {
            return;
        }

        AppHost host = _host;

        _titleBarButton = new TitleBarButtonWindow(
            host.WindowManager,
            host.VirtualDesktops,
            async (command, target) =>
            {
                OperationResult result = await host
                    .ExecuteAsync(command, CommandContext.FromUi(target))
                    .ConfigureAwait(false);

                // Our own commands change what the button shows and where it has to sit: the tick
                // of "always on top", and the stacking order when the window joins the topmost band.
                host.WindowTracker.Refresh();
                return result;
            },
            ShowMainWindow,
            () => host.Settings.TitleBarButton,
            _remember,
            message => _tray?.Notify(AppVersion.ProductName, message));

        host.WindowTracker.AnchorChanged += (_, e) =>
            Dispatcher.BeginInvoke(new Action(() => _titleBarButton?.Update(e.Anchor)));

        host.WindowTracker.Refresh();
    }

    private void ShowMainWindow() => ShowMainWindow(null);

    /// <summary>
    /// Abre los ajustes, que están dentro de la ventana principal (desde el 2026-10-10 no son otra
    /// ventana); con <paramref name="section"/>, en esa sección (por ejemplo «hotkeys»).
    /// </summary>
    private void ShowMainWindow(string? section)
    {
        ShowVdesk();
        _vdesk?.MostrarAjustes(section);
    }

    private void OnPauseRules(bool paused)
    {
        if (_host is null)
        {
            return;
        }

        _host.RuleApplication.Paused = paused;

        if (_viewModel is not null)
        {
            Dispatcher.BeginInvoke(new Action(() => _viewModel.RulesPaused = paused));
        }

        _tray?.UpdateIcon(paused);
    }

    /// <summary>
    /// A rule has just opened an application on another desktop: offer going there.
    /// </summary>
    /// <remarks>
    /// The event arrives on the worker thread of the rule engine, so it is handed to the
    /// interface thread. Only one question is shown at a time: an application that opens four
    /// windows at once must not produce four toasts.
    /// </remarks>
    private void OnRuleMovedWindow(object? sender, WindowMovedEventArgs e)
    {
        if (_host?.Settings.AskToFollowRuleMoves != true)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_followToast is { IsVisible: true })
            {
                return;
            }

            AppHost host = _host!;
            Guid desktop = e.Desktop.Id;
            VirtualDesktopInfo? origin = host.VirtualDesktops.GetCurrentDesktop();

            if (origin is null || origin.Id == desktop)
            {
                return;
            }

            bool accepted = false;

            _followToast = new FollowMoveToast(
                e.Window,
                e.Desktop,
                () =>
                {
                    accepted = true;
                    host.Logger.Info("FollowMoveAccepted", ("Desktop", e.Desktop.Number));
                    host.VirtualDesktops.SwitchToDesktop(desktop);
                })
            {
                DesktopId = desktop
            };

            DispatcherTimer guard = StartFollowGuard(host, origin.Id, desktop, () => accepted);

            _followToast.Closed += (_, _) => _followToast = null;
            _followToast.Show();
            guard.Start();
        }));
    }

    /// <summary>
    /// Keeps the user on their desktop until they say otherwise.
    /// </summary>
    /// <remarks>
    /// Moving a window that belongs to an application still starting up ends with Windows
    /// switching desktops on its own: when the application finishes loading it activates its own
    /// window, which by then lives somewhere else, and the view follows it. Measured on this
    /// machine, the jump lands about five seconds after the window first appears, and it does not
    /// happen at all if the window is moved once the application has settled.
    ///
    /// Since neither the moment the application finishes starting nor its decision to activate
    /// itself are ours to control, the jump is undone instead: if the view ends up on the offered
    /// desktop without the question having been answered, it goes back. A couple of corrections
    /// at most, so this can never turn into a fight with the user.
    /// </remarks>
    private DispatcherTimer StartFollowGuard(AppHost host, Guid origin, Guid target, Func<bool> accepted)
    {
        const int maximumCorrections = 3;

        DispatcherTimer guard = new(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        DateTime until = DateTime.UtcNow.AddSeconds(15);
        int corrections = 0;

        guard.Tick += (_, _) =>
        {
            if (accepted() || DateTime.UtcNow > until)
            {
                guard.Stop();
                return;
            }

            Guid? now = host.VirtualDesktops.GetCurrentDesktop()?.Id;

            if (now is null || now == origin)
            {
                return;
            }

            if (now == target && corrections < maximumCorrections)
            {
                corrections++;
                host.VirtualDesktops.SwitchToDesktop(origin);

                host.Logger.Info(
                    "FollowMoveGuard",
                    ("Reason", "Windows followed the window on its own"),
                    ("Correction", corrections));

                return;
            }

            // The user went somewhere else on their own: stop getting in the way.
            guard.Stop();
        };

        return guard;
    }

    /// <summary>
    /// Writes the rule for the window the user was on, taking the desktop it is on right now.
    /// </summary>
    private void RememberActiveWindow()
    {
        if (_host is null || _remember is null)
        {
            return;
        }

        nint target = TrayTargetWindow();

        if (target == 0)
        {
            return;
        }

        VirtualDesktopInfo? desktop = _remember.CurrentDesktopOf(target);

        if (desktop is null)
        {
            _tray?.Notify(AppVersion.ProductName, Loc.Get("Overlay.RequiresAdvancedBackend"));
            return;
        }

        string? name = _remember.Remember(target, new WindowActions { Desktop = DesktopResolver.Describe(desktop) });

        if (name is not null)
        {
            _tray?.Notify(AppVersion.ProductName, Loc.Format("Overlay.RuleSaved", name));
        }
    }

    /// <summary>
    /// Window a tray entry should act on. Clicking a tray icon hands the foreground to the shell,
    /// so asking Windows which window is in front would give the wrong answer; the tracker
    /// remembers the one the user was actually on.
    /// </summary>
    private nint TrayTargetWindow()
    {
        nint tracked = _host?.WindowTracker.LastTrackedWindow ?? 0;

        return tracked != 0 ? tracked : _host?.WindowManager.GetForegroundWindow() ?? 0;
    }

    private void RunTrayCommand(CommandType type)
    {
        if (_host is null)
        {
            return;
        }

        AppHost host = _host;
        nint target = TrayTargetWindow();

        _ = Task.Run(async () =>
        {
            OperationResult result = await host
                .ExecuteAsync(new CommandDefinition(type), new CommandContext(CommandSource.Tray, target))
                .ConfigureAwait(false);

            if (result.Status == OperationStatus.Unsupported)
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                    _tray?.Notify(AppVersion.ProductName, Loc.Get("Tray.NotAvailableYet"))));
            }
        });
    }


    /// <summary>
    /// Señal con la que otro programa puede pedirle a la aplicación que se cierre <b>bien</b>.
    /// </summary>
    /// <remarks>
    /// Hace falta porque matar el proceso no es lo mismo que cerrarlo: al salir, la aplicación guarda la
    /// última distribución de ventanas y le devuelve las esquinas y el borde a las ventanas que
    /// tocó al encajarlas. Un proceso muerto no hace ninguna de las dos cosas, y deja ventanas
    /// ajenas cuadradas hasta que su programa se reinicie.
    ///
    /// Y cerrar la ventana tampoco vale: cerrar los ajustes esconde la aplicación en la bandeja,
    /// que es lo que tiene que hacer. La única salida de verdad es la del menú de la bandeja, y
    /// hasta ahora no había forma de pedirla desde fuera.
    ///
    /// Lo usa <c>installer\desplegar.ps1</c> para cambiar de versión sin dejar rastro.
    /// </remarks>
    private const string QuitEventName = @"Local\VirtualDeskManager.Quit";

    private EventWaitHandle? _quitSignal;
    private RegisteredWaitHandle? _quitWait;

    private void ListenForQuitSignal()
    {
        try
        {
            _quitSignal = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);

            _quitWait = ThreadPool.RegisterWaitForSingleObject(
                _quitSignal,
                (_, timedOut) =>
                {
                    if (!timedOut)
                    {
                        // Llega en un hilo del grupo: la salida tiene que hacerse en el de la
                        // interfaz, que es el dueño de las ventanas y de los enganches.
                        _ = Dispatcher.BeginInvoke(new Action(ExitApplication));
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: true);
        }
        catch (Exception ex)
        {
            // Sin la señal, la aplicacion funciona igual: solo se pierde poder cerrarla desde un
            // script. No es motivo para no arrancar.
            _host?.Logger.Warn(LogEvents.NativeApiError, ex.Message, ("Where", nameof(ListenForQuitSignal)));
        }
    }

    /// <summary>
    /// La señal con nombre con la que se le pide a la copia en marcha que abra su ventana de ajustes.
    /// </summary>
    /// <remarks>
    /// La manda una segunda copia al arrancar (abrir otra vez el programa desde el menú Inicio o el
    /// acceso directo), y también se puede mandar desde un script, como hace la herramienta de
    /// capturas de los ajustes. Se rearma sola: vale para muchas veces.
    /// </remarks>
    private const string ShowSettingsEventName = @"Local\VirtualDeskManager.ShowSettings";

    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;

    private void ListenForShowSettingsSignal()
    {
        try
        {
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);

            _showWait = ThreadPool.RegisterWaitForSingleObject(
                _showSignal,
                (_, timedOut) =>
                {
                    if (!timedOut)
                    {
                        _ = Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception ex)
        {
            // Sin la señal, abrir otra vez el programa enseña el aviso de siempre. No es motivo para no arrancar.
            _host?.Logger.Warn(LogEvents.NativeApiError, ex.Message, ("Where", nameof(ListenForShowSettingsSignal)));
        }
    }

    private Views.Enlace.VentanaVdesk? _vdesk;
    private DatosVentana? _datosVentana;

    /// <summary>La ventana principal: una sola, se trae al frente si ya está.</summary>
    private void ShowVdesk()
    {
        if (_host is null)
        {
            return;
        }

        if (_vdesk is { IsLoaded: true })
        {
            if (_vdesk.WindowState == WindowState.Minimized)
            {
                _vdesk.WindowState = WindowState.Normal;
            }

            _vdesk.Activate();
            return;
        }

        _datosVentana ??= new DatosVentana(_host, s => ShowMainWindow(s), _viewModel);
        MainViewModel ajustes = _viewModel ?? throw new InvalidOperationException("Sin modelo de los ajustes.");
        _vdesk = new Views.Enlace.VentanaVdesk(new VentanaVdeskViewModel(_datosVentana), crearAjustes: () => new Views.Settings.PanelAjustes(ajustes));
        _vdesk.Closed += (_, _) => _vdesk = null;
        _vdesk.Show();
    }

    /// <summary>Le pide a la copia en marcha que abra sus ajustes. Falso si no entiende la señal (una versión anterior).</summary>
    private static bool AskRunningCopyToShowSettings()
    {
        try
        {
            using EventWaitHandle senal = EventWaitHandle.OpenExisting(ShowSettingsEventName);
            return senal.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    private void ExitApplication()
    {
        if (_titleBarButton is not null)
        {
            _titleBarButton.Close();
            _titleBarButton = null;
        }

        if (_vdesk is not null)
        {
            _vdesk.Close();
        }

        Shutdown();
    }

    /// <summary>
    /// Libera todo lo que tiene Windows (ganchos, atajos, miniaturas, COM) y suelta el mutex de
    /// instancia única para que la siguiente copia pueda arrancar.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        // Hooks, hotkeys, thumbnails and COM must all be released here (spec sections 41 and 53).
        _quitWait?.Unregister(null);
        _quitSignal?.Dispose();
        _showWait?.Unregister(null);
        _showSignal?.Dispose();

        _indicator?.Dispose();
        _console?.Dispose();
        _grid?.Dispose();
        _updates?.Dispose();
        _tray?.Dispose();
        _host?.Dispose();

        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was never owned by this thread; nothing to release.
            }

            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _host?.Logger.Error("UnhandledUiException", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _host?.Logger.Error("UnhandledException", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _host?.Logger.Error("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}
