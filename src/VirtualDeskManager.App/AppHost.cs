using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Backup;
using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Commands.Handlers;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Layout;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using VirtualDeskManager.Core.Zones;
using VirtualDeskManager.Windows.Events;
using VirtualDeskManager.Windows.Hotkeys;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Navigation;
using VirtualDeskManager.Windows.Overlay;
using VirtualDeskManager.Windows.Startup;
using VirtualDeskManager.Windows.VirtualDesktops;
using VirtualDeskManager.Windows.Windows;

namespace VirtualDeskManager.App;

/// <summary>
/// Composition root and lifetime owner of every service (spec sections 5 and 47).
/// The UI talks to this class, never to Win32.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly uint _ownProcessId;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Prepara las carpetas, el diario, la configuración y las copias de seguridad. El resto de
    /// servicios no se crea hasta <see cref="Start"/>.
    /// </summary>
    /// <param name="configurationDirectory">Carpeta de configuración; si es <c>null</c>, la de siempre.</param>
    public AppHost(string? configurationDirectory = null)
    {
        _ownProcessId = (uint)Environment.ProcessId;

        Paths = new ConfigurationPaths(configurationDirectory);
        Paths.EnsureCreated();

        Logger = new FileAppLogger(Paths.Logs);
        Configuration = new JsonConfigurationService(Paths, Logger);

        // Las copias se enganchan antes de que nadie escriba nada: la primera escritura de la
        // sesión es una de las que más interesa poder deshacer.
        Backup = new ConfigurationBackupService(
            Paths,
            () => Configuration.Settings.Backup,
            typeof(AppHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Logger);

        Configuration.History = Backup;
    }

    /// <summary>Dónde viven la configuración, el diario, los idiomas y demás ficheros.</summary>
    public ConfigurationPaths Paths { get; }

    /// <summary>El diario de la aplicación, en la carpeta de registros.</summary>
    public FileAppLogger Logger { get; }

    /// <summary>Ajustes, atajos y reglas, leídos y guardados en JSON.</summary>
    public JsonConfigurationService Configuration { get; }

    /// <summary>Copias de la configuración antes de cada escritura, para poder deshacerla.</summary>
    public ConfigurationBackupService Backup { get; }

    /// <summary>El hilo con bomba de mensajes Win32 del que tiran atajos, ganchos y eventos.</summary>
    /// <remarks>Como el resto de servicios de abajo, no existe hasta <see cref="Start"/>.</remarks>
    public Win32MessageLoop MessageLoop { get; private set; } = null!;

    /// <summary>Consultar y mover ventanas.</summary>
    public IWindowManager WindowManager { get; private set; } = null!;

    /// <summary>Los escritorios virtuales de Windows.</summary>
    public IVirtualDesktopService VirtualDesktops { get; private set; } = null!;

    /// <summary>Cambiar de escritorio; según los ajustes, con <c>SendInput</c> o sin él.</summary>
    public IDesktopNavigationService Navigation { get; private set; } = null!;

    /// <summary>Los atajos de teclado globales.</summary>
    public HotkeyManager Hotkeys { get; private set; } = null!;

    /// <summary>Avisos de Windows sobre ventanas que se crean, se muestran o cambian.</summary>
    public WinEventWindowEventService WindowEvents { get; private set; } = null!;

    /// <summary>Sigue a la ventana activa; de él tira el botón de la barra de título.</summary>
    public ActiveWindowTracker WindowTracker { get; private set; } = null!;

    /// <summary>Decide qué regla casa con cada ventana.</summary>
    public RuleEngine RuleEngine { get; private set; } = null!;

    /// <summary>Aplica las reglas a las ventanas nuevas y, si se pide, a las que ya había.</summary>
    public RuleApplicationService RuleApplication { get; private set; } = null!;

    /// <summary>Reparte cada orden a su manejador.</summary>
    public CommandDispatcher Commands { get; private set; } = null!;

    /// <summary>Arrancar con Windows o no.</summary>
    public IStartupService Startup { get; private set; } = null!;

    /// <summary>Recuerda los escritorios y repone los que Windows haya perdido.</summary>
    public DesktopLayoutService DesktopLayout { get; private set; } = null!;

    /// <summary>Recuerda donde estaba cada ventana y las repone tras un cambio de pantallas.</summary>
    public WindowLayoutService WindowLayout { get; private set; } = null!;

    /// <summary>Encaja la ventana en una zona al soltarla con Mayus pulsada.</summary>
    public ZoneSnapService ZoneSnap { get; private set; } = null!;

    /// <summary>Los distintivos de escritorio sobre los botones de la barra de tareas.</summary>
    public TaskbarBadgeService TaskbarBadges { get; private set; } = null!;

    /// <summary>Cuadra las esquinas de lo encajado y se acuerda de como estaban.</summary>
    public WindowEdgeKeeper ZoneEdges { get; private set; } = null!;

    /// <summary>The overview this application draws itself, once the interface has provided it.</summary>
    public IDesktopGridView? DesktopGrid { get; private set; }

    /// <summary>The shell that drops down from the top of the screen, likewise.</summary>
    public IDropDownConsole? DropDownConsole { get; private set; }

    /// <summary>Atajo a los ajustes cargados ahora mismo.</summary>
    public AppSettings Settings => Configuration.Settings;

    /// <summary>Raised whenever the configuration is reloaded so the UI can refresh.</summary>
    public event EventHandler? ConfigurationChanged;

    /// <summary>
    /// Carga la configuración, crea todos los servicios y los pone en marcha. Llamarlo más de una
    /// vez no hace nada.
    /// </summary>
    /// <remarks>
    /// Las funciones opcionales (distintivos de la barra de tareas) van envueltas: si fallan
    /// se apunta en el diario, pero el gestor arranca igual.
    /// </remarks>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        Configuration.Load();
        ApplyLoggingSettings();
        ApplyLanguage();

        // Para que los atajos se lean con lo que hay impreso en la tecla ("º") y no con el nombre
        // que le da Windows ("Oem5"), que depende de la distribución y no le dice nada a nadie.
        HotkeyDefinition.KeyNameResolver = KeyNames.Resolve;

        MessageLoop = new Win32MessageLoop(Logger);
        WindowManager = new Win32WindowManager(Logger);
        VirtualDesktops = VirtualDesktopServiceFactory.Create(Logger);
        Navigation = new DesktopNavigationService(
            VirtualDesktops,
            new SendInputDesktopNavigationService(
                Configuration.Settings.DesktopNavigation == DesktopNavigationMode.SendInput,
                Logger));

        DesktopLayout = new DesktopLayoutService(VirtualDesktops, Configuration, Logger);

        Startup = new RegistryStartupService(Logger);

        Commands = new CommandDispatcher(Logger);
        Commands.Register(new ToggleAlwaysOnTopCommandHandler(WindowManager, Logger));
        Commands.Register(new SwitchDesktopCommandHandler(Navigation));
        Commands.Register(new TaskViewCommandHandler(new TaskViewService(Logger)));
        Commands.Register(new DesktopCommandHandler(
            WindowManager,
            VirtualDesktops,
            () => Configuration.Settings.FollowMovedWindow,
            Logger));
        ZoneEdges = new WindowEdgeKeeper(WindowManager);
        Commands.Register(new ZoneCommandHandler(WindowManager, () => Configuration.Settings.Zones, ZoneEdges, Logger));
        Commands.Register(new WindowLayoutCommandHandler(() => WindowLayout.Restore(), Logger));

        Hotkeys = new HotkeyManager(MessageLoop, Logger);
        Hotkeys.HotkeyTriggered += OnHotkeyTriggered;
        AddMissingDefaultHotkeys();
        RegisterHotkeys();

        WindowLayout = new WindowLayoutService(
            // El de la interfaz explicitamente: los temporizadores de este servicio necesitan un
            // hilo con bomba de mensajes, y CurrentDispatcher a secas seria el hilo que resulte
            // estar llamando, que hoy es el correcto por casualidad y manana puede no serlo.
            System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher,
            WindowManager,
            new WindowLayoutRepository(Paths, Logger),
            () => Configuration.Settings.WindowLayout,
            MessageLoop,
            _ownProcessId,
            Logger);

        ZoneSnap = new ZoneSnapService(
            System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher,
            WindowManager,
            () => Configuration.Settings.Zones,
            MessageLoop,
            ZoneEdges,
            Logger);

        WindowEvents = new WinEventWindowEventService(MessageLoop, WindowManager, Logger);

        RuleEngine = new RuleEngine(Logger);
        RuleEngine.SetRules(Configuration.Rules);

        RuleApplication = new RuleApplicationService(
            RuleEngine,
            WindowManager,
            VirtualDesktops,
            WindowEvents,
            _ownProcessId,
            Logger);

        WindowTracker = new ActiveWindowTracker(MessageLoop, WindowManager, _ownProcessId, Logger);
        ApplyTitleBarButtonSettings();

        // Detras de VirtualDesktops y WindowManager, que es de lo que tira. Y envuelto, porque una
        // funcion opcional no puede impedir que arranque el gestor entero.
        TaskbarBadges = new TaskbarBadgeService(
            System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher,
            WindowManager,
            VirtualDesktops,
            () => Configuration.Settings.TaskbarBadges,
            Logger);

        WindowEvents.Start();

        // Before anything reads the desktops: the rules name them by number, so a set that Windows
        // has lost has to be back in place before the first rule is evaluated.
        RestoreDesktopLayout();

        WindowLayout.Start();
        ZoneSnap.Apply();
        RuleApplication.Start();

        // Envuelto, porque una función opcional no puede impedir que arranque el gestor entero.
        try
        {
            TaskbarBadges.Apply();
        }
        catch (Exception ex)
        {
            Logger.Error(LogEvents.CommandFailed, ex, ("Where", nameof(TaskbarBadges)));
        }

        if (Configuration.Settings.ApplyRulesOnStartup)
        {
            // Off the UI thread: enumerating every window takes a few milliseconds too many.
            Task.Run(() =>
            {
                try
                {
                    RuleApplication.ApplyToExistingWindows();
                }
                catch (Exception ex)
                {
                    Logger.Error(LogEvents.RuleFailed, ex, ("Where", nameof(RuleApplication.ApplyToExistingWindows)));
                }
            });
        }

        Logger.Info(
            LogEvents.ApplicationStarted,
            ("Version", AppVersion.Display),
            ("Backend", VirtualDesktops.BackendName),
            ("Configuration", Paths.Root));
    }

    /// <summary>Reloads settings, hotkeys and rules from disk (tray "Reload Configuration").</summary>
    public void ReloadConfiguration()
    {
        Configuration.Reload();
        ApplyLoggingSettings();
        ApplyLanguage();

        Hotkeys.UnregisterAll();
        RegisterHotkeys();

        RuleEngine.SetRules(Configuration.Rules);
        ApplyTitleBarButtonSettings();

        ZoneSnap?.Apply();

        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Single entry point used by the tray menu and the UI (architecture rules 2 to 4).</summary>
    public Task<OperationResult> ExecuteAsync(CommandDefinition command, CommandContext context) =>
        Commands.DispatchAsync(command, context);

    /// <summary>Lo mismo, para una orden sin parámetros.</summary>
    public Task<OperationResult> ExecuteAsync(CommandType type, CommandSource source) =>
        Commands.DispatchAsync(new CommandDefinition(type), new CommandContext(source));

    private void RegisterHotkeys()
    {
        Hotkeys.InterceptReserved = Configuration.Settings.InterceptReservedHotkeys;

        foreach (HotkeyDefinition hotkey in Configuration.Hotkeys)
        {
            Hotkeys.Register(hotkey);
        }

        // Al final y de una vez: mientras el gancho esta puesto, todas las teclas del equipo pasan
        // por el, asi que se toca lo menos posible.
        Hotkeys.ApplyHookedCombinations();
    }

    /// <summary>
    /// Puts back the desktops Windows lost, so that the rules that name them still find them.
    /// </summary>
    private void RestoreDesktopLayout()
    {
        try
        {
            DesktopLayoutService.RestoreOutcome outcome = DesktopLayout.Restore();

            if (outcome.Skipped)
            {
                Logger.Debug(LogEvents.DesktopOperation, ("Operation", "RestoreLayout"), ("Skipped", outcome.Reason ?? "?"));
                return;
            }

            if (outcome.Created == 0 && outcome.Renamed == 0)
            {
                DesktopLayout.Remember();
            }
        }
        catch (Exception ex)
        {
            // Never worth failing to start over: the application works with whatever desktops
            // happen to exist.
            Logger.Error(LogEvents.DesktopOperationFailed, ex, ("Where", nameof(RestoreDesktopLayout)));
        }
    }

    /// <summary>
    /// Hands over the overview of desktops and makes its commands available.
    /// </summary>
    /// <remarks>
    /// It arrives after <see cref="Start"/> rather than being built inside it, because it is a
    /// WPF window and this class knows nothing about WPF. Until the interface supplies one, the
    /// commands answer <see cref="OperationStatus.Unsupported"/> like any other command with no
    /// handler, which is exactly what should happen.
    /// </remarks>
    public void AttachDesktopGrid(IDesktopGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        DesktopGrid = grid;
        Commands.Register(new DesktopGridCommandHandler(grid));
    }

    /// <summary>Hands over the drop down console and makes its command available.</summary>
    public void AttachDropDownConsole(IDropDownConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        DropDownConsole = console;
        Commands.Register(new DropDownConsoleCommandHandler(console));
    }

    private void AddMissingDefaultHotkeys()
    {
        List<HotkeyDefinition> hotkeys = Configuration.Hotkeys.ToList();

        // Unconditional, and before anything else: the first version of these shortcuts used
        // Ctrl+Alt, which is exactly what AltGr sends, so they were eating |, @, # and ~ on every
        // Spanish keyboard. Leaving that in place for one more session is not an option.
        List<HotkeyDefinition> moved = HotkeyMigrations.RepairNumberedDesktopHotkeys(hotkeys).ToList();

        // La consola desplegable salió atada a OemTilde, que en un teclado español es la Ñ y no
        // la tecla de debajo de Escape que prometía la documentación.
        moved.AddRange(HotkeyMigrations.RepairDropDownConsoleHotkey(hotkeys));

        if (moved.Count > 0)
        {
            Configuration.SaveHotkeys(hotkeys);
            Logger.Info(
                LogEvents.ConfigurationSaved,
                ("Moved", moved.Count),
                ("What", "shortcuts that shipped on the wrong keys, moved to the right ones"),
                ("Which", string.Join(", ", moved.Select(h => $"{h.Id} -> {h.DisplayText}"))));
            hotkeys = Configuration.Hotkeys.ToList();
        }

        List<HotkeyDefinition> added = new();

        if (!Configuration.Settings.NumberedDesktopHotkeysAdded)
        {
            added.AddRange(HotkeyMigrations.AddNumberedDesktopHotkeys(hotkeys));
            Configuration.Settings.NumberedDesktopHotkeysAdded = true;
        }

        if (!Configuration.Settings.TaskViewHotkeysAdded)
        {
            added.AddRange(HotkeyMigrations.AddTaskViewHotkeys(hotkeys));
            Configuration.Settings.TaskViewHotkeysAdded = true;
        }

        if (!Configuration.Settings.DesktopGridHotkeyAdded)
        {
            added.AddRange(HotkeyMigrations.AddDesktopGridHotkeys(hotkeys));
            Configuration.Settings.DesktopGridHotkeyAdded = true;
        }

        if (!Configuration.Settings.DropDownConsoleHotkeyAdded)
        {
            added.AddRange(HotkeyMigrations.AddDropDownConsoleHotkeys(hotkeys));
            Configuration.Settings.DropDownConsoleHotkeyAdded = true;
        }

        if (!Configuration.Settings.DropDownConsolePinHotkeyAdded)
        {
            added.AddRange(HotkeyMigrations.AddDropDownConsolePinHotkeys(hotkeys));
            Configuration.Settings.DropDownConsolePinHotkeyAdded = true;
        }


        // The flags go up either way. If a combination was already taken there is nothing to add,
        // and trying again on every start would just be noise.
        Configuration.SaveSettings();

        if (added.Count == 0)
        {
            return;
        }

        Configuration.SaveHotkeys(hotkeys);

        Logger.Info(
            LogEvents.ConfigurationSaved,
            ("Added", added.Count),
            ("What", string.Join(", ", added.Select(h => h.Id))));
    }

    private void ApplyLoggingSettings()
    {
        LoggingSettings logging = Configuration.Settings.Logging;
        Logger.MinimumLevel = logging.Enabled ? logging.Level : LogLevel.None;
    }

    /// <summary>
    /// Saves the shortcuts and registers them again straight away, so an edit takes effect
    /// without restarting.
    /// </summary>
    public void ApplyHotkeys(IEnumerable<HotkeyDefinition> hotkeys)
    {
        Configuration.SaveHotkeys(hotkeys);
        Hotkeys.UnregisterAll();
        RegisterHotkeys();
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the rules and hands them to the engine, again with no restart.</summary>
    public void ApplyRules(IEnumerable<WindowRule> rules)
    {
        Configuration.SaveRules(rules);
        RuleEngine.SetRules(Configuration.Rules);
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts or stops the title bar button according to the settings.</summary>
    public void ApplyTitleBarButtonSettings()
    {
        if (WindowTracker is null)
        {
            return;
        }

        Configuration.Settings.TitleBarButton.Normalise();

        WindowTracker.ExtraOffset = Configuration.Settings.TitleBarButtonOffset;
        WindowTracker.SizePercent = Configuration.Settings.TitleBarButton.SizePercent;

        if (Configuration.Settings.ShowTitleBarButton)
        {
            WindowTracker.Start();
            WindowTracker.Refresh();
        }
        else
        {
            WindowTracker.Stop();
        }
    }

    private void ApplyLanguage()
    {
        // Files dropped in the lang folder win over the built in translations.
        Loc.Instance.OverrideDirectory = Paths.Languages;
        Loc.Instance.Use(Configuration.Settings.Language);

        Logger.Debug(
            "LanguageSelected",
            ("Requested", Configuration.Settings.Language),
            ("Applied", Loc.Instance.Current.EnglishName));
    }

    private void OnHotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        // The event arrives on the Win32 message loop thread: it must return immediately so the
        // pump keeps delivering messages (spec sections 37 and 38).
        CommandDefinition command = e.Hotkey.Command;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteAsync(command, CommandContext.FromHotkey()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(LogEvents.CommandFailed, ex, ("Hotkey", e.Hotkey.Id));
            }
        });
    }

    /// <summary>Para y libera todos los servicios. El diario se cierra el último.</summary>
    /// <remarks>
    /// La distribución de ventanas va primero: al cerrarse la guarda y aún necesita poder consultar
    /// las ventanas.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Antes que nada: al cerrar guarda la ultima distribucion, y para eso necesita que
            // las ventanas todavia se puedan consultar.
            WindowLayout?.Dispose();
            ZoneSnap?.Dispose();
            TaskbarBadges?.Dispose();

            WindowTracker?.Dispose();
            RuleApplication?.Dispose();
            WindowEvents?.Dispose();

            if (Hotkeys is not null)
            {
                Hotkeys.HotkeyTriggered -= OnHotkeyTriggered;
                Hotkeys.Dispose();
            }

            (VirtualDesktops as IDisposable)?.Dispose();
            MessageLoop?.Dispose();

            Logger.Info(LogEvents.ApplicationStopped);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            Logger.Dispose();
        }
    }
}
