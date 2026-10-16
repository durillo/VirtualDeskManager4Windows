using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using VirtualDeskManager.App.Localization;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Backup;
using VirtualDeskManager.Core.Zones;
using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>
/// View model of the whole settings window. It only talks to <see cref="AppHost"/> services,
/// never to Win32 (architecture rule 1).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly Dispatcher _dispatcher;

    private string _activeProcess = "-";
    private string _activeTitle = "-";
    private string _activeHandle = "-";
    private string _activeDesktopId = "-";
    private string _activeAlwaysOnTop = "-";
    private string _activePinned = "-";
    private bool _rulesPaused;
    private string _lastAction = "";
    private LanguageOption _selectedLanguage;
    private CloudFolderOption _selectedCloudFolder = CloudFolderOption.None();

    /// <summary>
    /// Prepara la ventana de ajustes, con su pestaña del diario, y se queda
    /// escuchando los cambios de configuración, de ventana activa y de idioma.
    /// </summary>
    /// <param name="host">La aplicación, con todos sus servicios y ajustes.</param>
    /// <param name="dispatcher">El hilo de la interfaz, al que se pasan los avisos que llegan de otros.</param>
    public MainViewModel(AppHost host, Dispatcher dispatcher)
    {
        _host = host;
        _dispatcher = dispatcher;

        Diary = new DiaryViewModel(host.Logger, dispatcher);

        RefreshCommand = new RelayCommand(RefreshActiveWindow);
        ReloadCommand = new RelayCommand(() => _host.ReloadConfiguration());
        OpenConfigFolderCommand = new RelayCommand(() => OpenFolder(_host.Paths.Root));
        OpenLogFolderCommand = new RelayCommand(() => OpenFolder(_host.Paths.Logs));
        ToggleAlwaysOnTopCommand = new RelayCommand(ToggleAlwaysOnTop);

        AddHotkeyCommand = new RelayCommand(AddHotkey);
        EditHotkeyCommand = new RelayCommand(EditSelectedHotkey, () => SelectedHotkey is not null);
        DeleteHotkeyCommand = new RelayCommand(DeleteSelectedHotkey, () => SelectedHotkey is not null);

        RefreshDesktopsCommand = new RelayCommand(RefreshDesktops);
        SaveDesktopNamesCommand = new RelayCommand(SaveDesktopNames, () => _host.VirtualDesktops.Capabilities.CanRenameDesktop);

        AddRuleCommand = new RelayCommand(AddRule);
        EditRuleCommand = new RelayCommand(EditSelectedRule, () => SelectedRule is not null);
        DeleteRuleCommand = new RelayCommand(DeleteSelectedRule, () => SelectedRule is not null);

        BackupNowCommand = new RelayCommand(BackupNow);
        RestoreBackupCommand = new RelayCommand(RestoreBackup);
        ExportBackupCommand = new RelayCommand(ExportBackup);
        ImportBackupCommand = new RelayCommand(ImportBackup);
        OpenBackupFolderCommand = new RelayCommand(() => OpenFolder(_host.Backup.HistoryDirectory));

        RestoreLayoutNowCommand = new RelayCommand(RestoreLayoutNow);
        ForgetLayoutsCommand = new RelayCommand(ForgetLayouts);
        AddZoneHotkeysCommand = new RelayCommand(AddZoneHotkeys);

        BuildCloudFolders();
        MatchColourPreset();
        RefreshZoneMonitors();

        Languages.Add(LanguageOption.Automatic());

        foreach (LanguageInfo language in Loc.Instance.AvailableLanguages)
        {
            Languages.Add(LanguageOption.For(language));
        }

        _selectedLanguage = Languages.FirstOrDefault(
                                l => string.Equals(l.Code, _host.Settings.Language, StringComparison.OrdinalIgnoreCase))
                            ?? Languages[0];

        _host.ConfigurationChanged += (_, _) => _dispatcher.BeginInvoke(new Action(RefreshEverything));
        _host.WindowEvents.ForegroundWindowChanged += OnForegroundChanged;
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        RefreshEverything();
    }

    // ------------------------------------------------------------------ dashboard

    /// <summary>El «Estado» del panel: siempre «En ejecución», porque si se ve es que lo está.</summary>
    public string Status => Loc.Get("Dashboard.Running");

    /// <summary>El «Motor» de escritorios virtuales en uso.</summary>
    public string BackendName => _host.VirtualDesktops.BackendName;

    /// <summary>La carpeta de configuración, que se enseña en los ajustes.</summary>
    public string ConfigurationDirectory => _host.Paths.Root;

    /// <summary>Para los diálogos que necesitan dejar rastro, como la captura de teclas.</summary>
    public IAppLogger Logger => _host.Logger;

    /// <summary>La versión de la aplicación, tal y como se enseña al usuario.</summary>
    public string Version => AppVersion.Display;

    /// <summary>Las «Capacidades» del panel: qué sabe hacer el motor de escritorios en este equipo.</summary>
    public ObservableCollection<CapabilityRow> Capabilities { get; } = new();

    /// <summary>El «Proceso» de la ventana activa, o «-» si no se pudo leer.</summary>
    public string ActiveProcess
    {
        get => _activeProcess;
        private set => Set(ref _activeProcess, value);
    }

    /// <summary>El «Título» de la ventana activa.</summary>
    public string ActiveTitle
    {
        get => _activeTitle;
        private set => Set(ref _activeTitle, value);
    }

    /// <summary>El «Identificador» (el handle) de la ventana activa.</summary>
    public string ActiveHandle
    {
        get => _activeHandle;
        private set => Set(ref _activeHandle, value);
    }

    /// <summary>El «ID de escritorio» de la ventana activa, o «No soportado» si el motor no lo sabe.</summary>
    public string ActiveDesktopId
    {
        get => _activeDesktopId;
        private set => Set(ref _activeDesktopId, value);
    }

    /// <summary>Si la ventana activa está «Siempre visible».</summary>
    public string ActiveAlwaysOnTop
    {
        get => _activeAlwaysOnTop;
        private set => Set(ref _activeAlwaysOnTop, value);
    }

    /// <summary>
    /// Si la ventana activa está «Fijada» en todos los escritorios. Ahora mismo no se consulta: dice
    /// «No» si el motor sabe fijar y «No soportado» si no.
    /// </summary>
    public string ActivePinned
    {
        get => _activePinned;
        private set => Set(ref _activePinned, value);
    }

    /// <summary>
    /// El resultado de lo último que se hizo desde la ventana, en la barra de abajo: un atajo
    /// guardado, una copia hecha, un error…
    /// </summary>
    public string LastAction
    {
        get => _lastAction;
        private set => Set(ref _lastAction, value);
    }

    // ------------------------------------------------------------------ actualizaciones

    private UpdatesViewModel? _updates;

    /// <summary>El grupo «Actualizaciones» de General; lo pone la aplicación al arrancar.</summary>
    public UpdatesViewModel? Updates
    {
        get => _updates;
        set => Set(ref _updates, value);
    }

    // ------------------------------------------------------------------ inicio

    /// <summary>
    /// Debajo de «En ejecución»: si el motor lo deja hacer todo o sólo lo básico.
    /// </summary>
    public string EngineSummary => Loc.Get(
        _host.VirtualDesktops.Capabilities.CanEnumerateDesktops ? "Home.EngineFull" : "Home.EngineBasic");

    /// <summary>El escritorio en el que se está, como «2 · Trabajo», o «-» si el motor no lo sabe.</summary>
    public string CurrentDesktopText =>
        Desktops.FirstOrDefault(d => d.IsCurrent) is { } actual
            ? string.IsNullOrWhiteSpace(actual.Name) ? actual.Number.ToString(CultureInfo.CurrentCulture) : $"{actual.Number} · {actual.Name}"
            : "-";

    /// <summary>Debajo del escritorio actual: cuántos hay.</summary>
    public string DesktopCountText => Loc.Format("Home.DesktopCount", Desktops.Count);

    /// <summary>Las automatizaciones en una línea: cuántas hay encendidas, o que están en pausa.</summary>
    public string AutomationsSummary => RulesPaused
        ? Loc.Get("Home.AutomationsPaused")
        : Loc.Format("Home.AutomationsActive", Rules.Count(r => r.Enabled));

    /// <summary>
    /// «Necesita tu atención» de Inicio: atajos que no funcionan y ficheros de configuración con
    /// errores, cada uno con la sección donde se arregla.
    /// </summary>
    public ObservableCollection<AttentionRow> Attention { get; } = new();

    /// <summary>Que no hay nada en <see cref="Attention"/>: Inicio dice «Todo en orden».</summary>
    public bool AllInOrder => Attention.Count == 0;

    private void RefreshAttention()
    {
        Attention.Clear();

        foreach (HotkeyRow row in Hotkeys.Where(h => h.IsConflict))
        {
            Attention.Add(new AttentionRow(Loc.Format("Attention.HotkeyConflict", row.Shortcut, row.Action), "hotkeys"));
        }

        foreach (IssueRow issue in Issues)
        {
            Attention.Add(new AttentionRow(Loc.Format("Attention.ConfigIssue", issue.File, issue.Message), "general"));
        }

        Raise(nameof(AllInOrder));
    }

    private void RaiseHome()
    {
        Raise(nameof(EngineSummary));
        Raise(nameof(CurrentDesktopText));
        Raise(nameof(DesktopCountText));
        Raise(nameof(AutomationsSummary));
    }

    // ------------------------------------------------------------------ lists

    /// <summary>The virtual desktops, with their names editable in place.</summary>
    public ObservableCollection<DesktopRow> Desktops { get; } = new();

    /// <summary>
    /// La lista de la pestaña de atajos: los del fichero, se hayan podido registrar o no, para que
    /// los que fallaron se puedan editar.
    /// </summary>
    public ObservableCollection<HotkeyRow> Hotkeys { get; } = new();

    /// <summary>La tabla de la pestaña de reglas.</summary>
    public ObservableCollection<RuleRow> Rules { get; } = new();

    /// <summary>Los «Problemas de configuración» del panel.</summary>
    public ObservableCollection<IssueRow> Issues { get; } = new();

    // ------------------------------------------------------------------ language

    /// <summary>Los idiomas del desplegable «Idioma»: primero «El mismo que Windows» y luego los que trae.</summary>
    public ObservableCollection<LanguageOption> Languages { get; } = new();

    /// <summary>El desplegable «Idioma». Al cambiarlo se guarda y la interfaz cambia de idioma en el acto.</summary>
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            // A collection refresh can momentarily push a null selection.
            if (value is null || !Set(ref _selectedLanguage, value))
            {
                return;
            }

            _host.Settings.Language = value.Code;
            _host.Configuration.SaveSettings();
            Loc.Instance.Use(value.Code);
        }
    }

    // ------------------------------------------------------------------ settings

    /// <summary>
    /// La casilla «Iniciar VirtualDeskManager con Windows». Al cambiarla se guarda y se da de alta
    /// o de baja el arranque con Windows.
    /// </summary>
    public bool StartWithWindows
    {
        get => _host.Settings.StartWithWindows;
        set
        {
            if (_host.Settings.StartWithWindows == value)
            {
                return;
            }

            _host.Settings.StartWithWindows = value;
            _host.Configuration.SaveSettings();

            if (value)
            {
                _ = _host.Startup.EnableAsync();
            }
            else
            {
                _ = _host.Startup.DisableAsync();
            }

            Raise();
        }
    }

    /// <summary>La casilla «Arrancar minimizado en la bandeja». Se guarda al cambiarla y vale para el próximo arranque.</summary>
    public bool StartMinimized
    {
        get => _host.Settings.StartMinimized;
        set => SetSetting(v => _host.Settings.StartMinimized = v, _host.Settings.StartMinimized, value);
    }

    /// <summary>La casilla «Seguir a la ventana al moverla a otro escritorio». Se guarda al cambiarla.</summary>
    public bool FollowMovedWindow
    {
        get => _host.Settings.FollowMovedWindow;
        set => SetSetting(v => _host.Settings.FollowMovedWindow = v, _host.Settings.FollowMovedWindow, value);
    }

    /// <summary>
    /// La casilla «Aplicar las reglas a las ventanas ya abiertas al arrancar». Se guarda al
    /// cambiarla y vale para el próximo arranque.
    /// </summary>
    public bool ApplyRulesOnStartup
    {
        get => _host.Settings.ApplyRulesOnStartup;
        set => SetSetting(v => _host.Settings.ApplyRulesOnStartup = v, _host.Settings.ApplyRulesOnStartup, value);
    }

    /// <summary>
    /// La casilla «Cuando una regla abra una aplicación en otro escritorio, preguntar si quiero
    /// ir». Se guarda al cambiarla.
    /// </summary>
    public bool AskToFollowRuleMoves
    {
        get => _host.Settings.AskToFollowRuleMoves;
        set => SetSetting(v => _host.Settings.AskToFollowRuleMoves = v, _host.Settings.AskToFollowRuleMoves, value);
    }

    /// <summary>
    /// La casilla «Mostrar un botón en la barra de título de la ventana activa». Al cambiarla se
    /// guarda y el botón aparece o desaparece en el acto.
    /// </summary>
    public bool ShowTitleBarButton
    {
        get => _host.Settings.ShowTitleBarButton;
        set
        {
            if (_host.Settings.ShowTitleBarButton == value)
            {
                return;
            }

            _host.Settings.ShowTitleBarButton = value;
            _host.Configuration.SaveSettings();
            _host.ApplyTitleBarButtonSettings();
            Raise();
        }
    }

    /// <summary>
    /// La casilla «Escribir fichero de registro». Al cambiarla se guarda y el registrador empieza o
    /// deja de escribir en el acto.
    /// </summary>
    public bool LoggingEnabled
    {
        get => _host.Settings.Logging.Enabled;
        set
        {
            if (_host.Settings.Logging.Enabled == value)
            {
                return;
            }

            _host.Settings.Logging.Enabled = value;
            _host.Logger.MinimumLevel = value ? _host.Settings.Logging.Level : LogLevel.None;
            _host.Configuration.SaveSettings();
            Raise();
        }
    }

    /// <summary>Los niveles que ofrece el desplegable «Nivel» del registro, del más hablador al más callado.</summary>
    public IReadOnlyList<LogLevel> LogLevels { get; } = new[]
    {
        LogLevel.Trace,
        LogLevel.Debug,
        LogLevel.Information,
        LogLevel.Warning,
        LogLevel.Error
    };

    /// <summary>The shells offered for the drop down console, in the order they are listed.</summary>
    public IReadOnlyList<ConsoleShell> ConsoleShells { get; } = new[]
    {
        ConsoleShell.Cmd,
        ConsoleShell.PowerShell,
        ConsoleShell.PowerShellCore,
        ConsoleShell.WindowsTerminal,
        ConsoleShell.Custom
    };

    /// <summary>La casilla «Activar la consola que baja desde arriba de la pantalla». Se guarda al cambiarla.</summary>
    public bool DropDownConsoleEnabled
    {
        get => _host.Settings.DropDownConsole.Enabled;
        set => SetSetting(
            v => _host.Settings.DropDownConsole.Enabled = v,
            _host.Settings.DropDownConsole.Enabled,
            value);
    }

    /// <summary>La casilla «Recogerla cuando deje de ser la ventana de delante». Se guarda al cambiarla.</summary>
    public bool DropDownConsoleHideWhenFocusLost
    {
        get => _host.Settings.DropDownConsole.HideWhenFocusLost;
        set => SetSetting(
            v => _host.Settings.DropDownConsole.HideWhenFocusLost = v,
            _host.Settings.DropDownConsole.HideWhenFocusLost,
            value);
    }

    /// <summary>Bajar la consola en la pantalla donde esté el ratón.</summary>
    public bool DropDownConsoleFollowMouse
    {
        get => _host.Settings.DropDownConsole.FollowMouse;
        set => SetSetting(
            v => _host.Settings.DropDownConsole.FollowMouse = v,
            _host.Settings.DropDownConsole.FollowMouse,
            value);
    }

    /// <summary>
    /// El desplegable «Intérprete» de la consola. Se guarda al cambiarlo; con
    /// <see cref="ConsoleShell.Custom"/> aparece el cuadro «Comando».
    /// </summary>
    public ConsoleShell SelectedConsoleShell
    {
        get => _host.Settings.DropDownConsole.Shell;
        set
        {
            if (_host.Settings.DropDownConsole.Shell == value)
            {
                return;
            }

            _host.Settings.DropDownConsole.Shell = value;
            _host.Configuration.SaveSettings();

            Raise(nameof(SelectedConsoleShell));
            Raise(nameof(ConsoleShellIsCustom));
        }
    }

    /// <summary>Whether the box for a command line of one's own is worth showing.</summary>
    public bool ConsoleShellIsCustom => _host.Settings.DropDownConsole.Shell == ConsoleShell.Custom;

    /// <summary>El cuadro «Comando»: la línea de órdenes propia para la consola. Se guarda sin espacios a los lados.</summary>
    public string ConsoleCustomCommand
    {
        get => _host.Settings.DropDownConsole.CustomCommand;
        set
        {
            string trimmed = value?.Trim() ?? "";

            if (_host.Settings.DropDownConsole.CustomCommand == trimmed)
            {
                return;
            }

            _host.Settings.DropDownConsole.CustomCommand = trimmed;
            _host.Configuration.SaveSettings();
            Raise(nameof(ConsoleCustomCommand));
        }
    }

    /// <summary>El deslizador «Ancho» de la consola, en porcentaje de la pantalla. Se guarda al cambiarlo.</summary>
    public int ConsoleWidthPercent
    {
        get => _host.Settings.DropDownConsole.WidthPercent;
        set => SetConsoleSetting(
            v => _host.Settings.DropDownConsole.WidthPercent = v,
            _host.Settings.DropDownConsole.WidthPercent,
            value);
    }

    /// <summary>El deslizador «Alto» de la consola, en porcentaje de la pantalla. Se guarda al cambiarlo.</summary>
    public int ConsoleHeightPercent
    {
        get => _host.Settings.DropDownConsole.HeightPercent;
        set => SetConsoleSetting(
            v => _host.Settings.DropDownConsole.HeightPercent = v,
            _host.Settings.DropDownConsole.HeightPercent,
            value);
    }

    /// <summary>El deslizador «Opacidad» de la consola, en porcentaje. Se guarda al cambiarlo.</summary>
    public int ConsoleOpacityPercent
    {
        get => _host.Settings.DropDownConsole.OpacityPercent;
        set => SetConsoleSetting(
            v => _host.Settings.DropDownConsole.OpacityPercent = v,
            _host.Settings.DropDownConsole.OpacityPercent,
            value);
    }

    /// <summary>
    /// El deslizador «Deslizamiento»: cuántos milisegundos tarda la consola en bajar o subir; cero
    /// es sin animación. Se guarda al cambiarlo.
    /// </summary>
    public int ConsoleAnimationMilliseconds
    {
        get => _host.Settings.DropDownConsole.AnimationMilliseconds;
        set => SetConsoleSetting(
            v => _host.Settings.DropDownConsole.AnimationMilliseconds = v,
            _host.Settings.DropDownConsole.AnimationMilliseconds,
            value);
    }

    /// <summary>
    /// El desplegable «Nivel» del registro. Se guarda al cambiarlo y, si el registro está
    /// encendido, se aplica en el acto.
    /// </summary>
    public LogLevel SelectedLogLevel
    {
        get => _host.Settings.Logging.Level;
        set
        {
            if (_host.Settings.Logging.Level == value)
            {
                return;
            }

            _host.Settings.Logging.Level = value;

            if (_host.Settings.Logging.Enabled)
            {
                _host.Logger.MinimumLevel = value;
            }

            _host.Configuration.SaveSettings();
            Raise();
        }
    }

    /// <summary>
    /// La casilla «Pausar reglas»: mientras está marcada no se aplica ninguna. No se guarda; se
    /// avisa con <see cref="RulesPausedChanged"/> para que el icono de la bandeja lo refleje.
    /// </summary>
    public bool RulesPaused
    {
        get => _rulesPaused;
        set
        {
            if (!Set(ref _rulesPaused, value))
            {
                return;
            }

            _host.RuleApplication.Paused = value;
            RulesPausedChanged?.Invoke(this, value);
            Raise(nameof(AutomationsSummary));
        }
    }

    /// <summary>Se pausaron o se reanudaron las reglas; el valor dice si quedan pausadas.</summary>
    public event EventHandler<bool>? RulesPausedChanged;

    /// <summary>Se ha tocado el ajuste del escritorio en la bandeja: hay que repintar el icono.</summary>
    public event EventHandler? DesktopIndicatorChanged;

    /// <summary>Mostrar el escritorio actual en el icono de la bandeja.</summary>
    public bool ShowDesktopInTray
    {
        get => _host.Settings.ShowDesktopInTray;
        set
        {
            if (_host.Settings.ShowDesktopInTray == value)
            {
                return;
            }

            _host.Settings.ShowDesktopInTray = value;
            _host.Configuration.SaveSettings();

            Raise(nameof(ShowDesktopInTray));
            DesktopIndicatorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ------------------------------------------------------------------ commands

    /// <summary>El botón «Actualizar» de la lista de escritorios: la vuelve a leer y descarta los nombres a medio editar.</summary>
    public RelayCommand RefreshDesktopsCommand { get; }

    /// <summary>
    /// El botón «Guardar» de los nombres de escritorio: renombra los que cambiaron y guarda la
    /// distribución para reponerla si Windows pierde los escritorios. Desactivado si el motor no
    /// sabe renombrar.
    /// </summary>
    public RelayCommand SaveDesktopNamesCommand { get; }

    /// <summary>La pestaña del diario, con su propio modelo de vista.</summary>
    public DiaryViewModel Diary { get; }

    /// <summary>El botón «Actualizar» del panel: vuelve a leer la ventana activa.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>
    /// Los botones «Recargar configuración» y «Recargar desde disco»: vuelven a leer los ficheros
    /// de configuración y lo aplican todo, atajos y reglas incluidos.
    /// </summary>
    public RelayCommand ReloadCommand { get; }

    /// <summary>El botón «Abrir carpeta de configuración», en el Explorador.</summary>
    public RelayCommand OpenConfigFolderCommand { get; }

    /// <summary>El botón «Abrir carpeta de registros», en el Explorador.</summary>
    public RelayCommand OpenLogFolderCommand { get; }

    /// <summary>El botón «Alternar Siempre visible» del panel, sobre la ventana activa.</summary>
    public RelayCommand ToggleAlwaysOnTopCommand { get; }

    /// <summary>El botón «Añadir» de la pestaña de atajos: abre el editor con un atajo nuevo.</summary>
    public RelayCommand AddHotkeyCommand { get; }

    /// <summary>El botón «Editar» de la pestaña de atajos. Desactivado sin un atajo elegido.</summary>
    public RelayCommand EditHotkeyCommand { get; }

    /// <summary>El botón «Eliminar» de la pestaña de atajos, tras preguntar. Desactivado sin un atajo elegido.</summary>
    public RelayCommand DeleteHotkeyCommand { get; }

    /// <summary>El botón «Añadir» de la pestaña de reglas: abre el editor con una regla nueva.</summary>
    public RelayCommand AddRuleCommand { get; }

    /// <summary>El botón «Editar» de la pestaña de reglas. Desactivado sin una regla elegida.</summary>
    public RelayCommand EditRuleCommand { get; }

    /// <summary>El botón «Eliminar» de la pestaña de reglas, tras preguntar. Desactivado sin una regla elegida.</summary>
    public RelayCommand DeleteRuleCommand { get; }

    /// <summary>El botón «Guardar copia ahora» de las copias de seguridad.</summary>
    public RelayCommand BackupNowCommand { get; }

    /// <summary>
    /// El botón «Restaurar...»: abre la lista de copias y, si se restaura una, vuelve a leer la
    /// configuración.
    /// </summary>
    public RelayCommand RestoreBackupCommand { get; }

    /// <summary>El botón «Exportar a un fichero...»: guarda la configuración en un <c>.vdesk</c> donde se elija.</summary>
    public RelayCommand ExportBackupCommand { get; }

    /// <summary>
    /// El botón «Importar de un fichero...»: restaura la configuración de un <c>.vdesk</c> y la
    /// vuelve a leer.
    /// </summary>
    public RelayCommand ImportBackupCommand { get; }

    /// <summary>El botón «Abrir la carpeta de copias», en el Explorador.</summary>
    public RelayCommand OpenBackupFolderCommand { get; }

    /// <summary>
    /// El botón «Recolocarlas ahora»: devuelve las ventanas a donde estaban con las pantallas de
    /// ahora, si hay algo guardado para ellas.
    /// </summary>
    public RelayCommand RestoreLayoutNowCommand { get; }

    /// <summary>El botón «Olvidar lo guardado»: borra las posiciones de ventana recordadas.</summary>
    public RelayCommand ForgetLayoutsCommand { get; }

    /// <summary>
    /// El botón «Crear los atajos de zona»: añade Win+Alt+1, 2… para mandar la ventana a cada
    /// zona, tantos como zonas tenga el monitor más repartido, sin tocar los que ya existan.
    /// </summary>
    public RelayCommand AddZoneHotkeysCommand { get; }

    // ------------------------------------------------------------------ editing

    private HotkeyRow? _selectedHotkey;
    private RuleRow? _selectedRule;

    /// <summary>El atajo elegido en la lista, sobre el que actúan Editar y Eliminar.</summary>
    public HotkeyRow? SelectedHotkey
    {
        get => _selectedHotkey;
        set
        {
            if (Set(ref _selectedHotkey, value))
            {
                EditHotkeyCommand.RaiseCanExecuteChanged();
                DeleteHotkeyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>La regla elegida en la tabla, sobre la que actúan Editar y Eliminar.</summary>
    public RuleRow? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (Set(ref _selectedRule, value))
            {
                EditRuleCommand.RaiseCanExecuteChanged();
                DeleteRuleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Set by the window: showing a dialog is its job, not the view model's.
    /// </summary>
    public Func<HotkeyEditorViewModel, HotkeyDefinition?>? EditHotkeyRequested { get; set; }

    /// <summary>
    /// Lo pone la ventana: abre el editor de reglas y devuelve la regla guardada, o <c>null</c> si
    /// se cancela.
    /// </summary>
    public Func<RuleEditorViewModel, WindowRule?>? EditRuleRequested { get; set; }

    /// <summary>Lo pone la ventana: hace una pregunta de sí o no y devuelve si se contestó que sí.</summary>
    public Func<string, bool>? ConfirmRequested { get; set; }

    private void AddHotkey() => EditHotkey(null);

    private void EditSelectedHotkey() => EditHotkey(SelectedHotkey?.Definition);

    private void EditHotkey(HotkeyDefinition? existing)
    {
        if (EditHotkeyRequested is null)
        {
            return;
        }

        List<HotkeyDefinition> all = _host.Configuration.Hotkeys.ToList();
        HotkeyEditorViewModel editor = new(existing, all);
        HotkeyDefinition? edited = EditHotkeyRequested(editor);

        if (edited is null)
        {
            return;
        }

        // The user agreed to take the combination from another shortcut. It is switched off
        // rather than deleted: it stays in the list saying what it was for, and giving it a new
        // combination is one edit away. Deleting it would be a surprise nobody asked for.
        // Pueden ser dos, uno por cada combinación del atajo que se acaba de guardar.
        List<string> displacedNotes = new();

        foreach (HotkeyDefinition displaced in editor.Displaced)
        {
            displaced.Enabled = false;
            displacedNotes.Add(Loc.Format("Hotkeys.Displaced", displaced.Id, UiText.Command(displaced.Command)));
        }

        int index = existing is null ? -1 : all.IndexOf(existing);

        if (index >= 0)
        {
            all[index] = edited;
        }
        else
        {
            all.Add(edited);
        }

        _host.ApplyHotkeys(all);

        LastAction = edited.DisplayText + "  ->  " + UiText.Command(edited.Command)
                     + (displacedNotes.Count == 0 ? "" : "   " + string.Join("   ", displacedNotes));
    }

    private void DeleteSelectedHotkey()
    {
        if (SelectedHotkey is not HotkeyRow row || ConfirmRequested is null)
        {
            return;
        }

        if (!ConfirmRequested(Loc.Format("Common.ConfirmDelete", row.Shortcut)))
        {
            return;
        }

        List<HotkeyDefinition> all = _host.Configuration.Hotkeys
            .Where(h => !ReferenceEquals(h, row.Definition))
            .ToList();

        _host.ApplyHotkeys(all);
    }

    /// <summary>
    /// Cómo se puede nombrar un escritorio en una regla: por su nombre si lo tiene, y por su
    /// número siempre.
    /// </summary>
    /// <remarks>
    /// Las dos formas valen en el campo de la regla, así que se ofrecen las dos en vez de obligar
    /// a elegir: quien ha bautizado sus escritorios pensará en "Correo", y quien no, en "3".
    /// </remarks>
    private List<string> DesktopNames()
    {
        List<string> names = new();

        foreach (VirtualDesktopInfo desktop in _host.VirtualDesktops.GetDesktops())
        {
            if (desktop.HasName)
            {
                names.Add(desktop.Name);
            }

            names.Add(desktop.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return names;
    }

    /// <summary>
    /// Los monitores del equipo, descritos de forma que se reconozcan.
    /// </summary>
    /// <remarks>
    /// Con la resolución y cuál es el principal, porque «Monitor 2» a secas no le dice a nadie
    /// cuál de los dos que tiene delante es. El orden es el que da Windows, y el número que se
    /// guarda en la regla es la posición en esa lista empezando por uno.
    /// </remarks>
    private List<MonitorChoice> MonitorChoices()
    {
        List<MonitorChoice> monitors = new();

        // Por el gestor de ventanas y no contándolos aquí: es la misma lista que usa el
        // movimiento, así que "Monitor 2" significa la misma pantalla en el desplegable y en la
        // regla. Contarlos por separado sería pedir que algún día dejaran de coincidir.
        foreach (MonitorInfo monitor in _host.WindowManager.GetMonitors())
        {
            monitors.Add(new MonitorChoice(
                monitor.Number,
                Loc.Format(
                    monitor.IsPrimary ? "RuleEditor.MonitorPrimary" : "RuleEditor.MonitorOther",
                    monitor.Number,
                    monitor.Bounds.Width,
                    monitor.Bounds.Height)));
        }

        return monitors;
    }

    private void AddRule() => EditRule(null);

    private void EditSelectedRule() => EditRule(SelectedRule?.Rule);

    private void EditRule(WindowRule? existing)
    {
        if (EditRuleRequested is null)
        {
            return;
        }

        // La lista se arma en el momento de abrir el editor y no antes: lo que interesa es lo que
        // el usuario tiene abierto ahora, que es como va a reconocer su aplicación.
        WindowRule? edited = EditRuleRequested(new RuleEditorViewModel(
            existing,
            RunningApplications.Collect(_host.WindowManager, (uint)Environment.ProcessId),
            DesktopNames(),
            MonitorChoices()));

        if (edited is null)
        {
            return;
        }

        List<WindowRule> all = _host.Configuration.Rules.ToList();
        int index = existing is null ? -1 : all.IndexOf(existing);

        if (index >= 0)
        {
            all[index] = edited;
        }
        else
        {
            all.Add(edited);
        }

        _host.ApplyRules(all);
        LastAction = edited.Name;
    }

    private void DeleteSelectedRule()
    {
        if (SelectedRule is not RuleRow row || ConfirmRequested is null)
        {
            return;
        }

        if (!ConfirmRequested(Loc.Format("Common.ConfirmDelete", row.Name)))
        {
            return;
        }

        List<WindowRule> all = _host.Configuration.Rules
            .Where(r => !ReferenceEquals(r, row.Rule))
            .ToList();

        _host.ApplyRules(all);
    }

    // ------------------------------------------------------------------ refresh

    /// <summary>
    /// Vuelve a leer todo lo que enseña la ventana: capacidades, atajos, reglas, problemas,
    /// escritorios, ventana activa y ajustes. Se llama al abrirla y cuando cambia la
    /// configuración o el idioma.
    /// </summary>
    public void RefreshEverything()
    {
        RefreshCapabilities();
        RefreshHotkeys();
        RefreshRules();
        RefreshIssues();
        RefreshDesktops();
        RefreshActiveWindow();
        RefreshAttention();
        RaiseHome();

        Raise(nameof(Status));
        Raise(nameof(BackendName));
        Raise(nameof(StartWithWindows));
        Raise(nameof(StartMinimized));
        Raise(nameof(FollowMovedWindow));
        Raise(nameof(ApplyRulesOnStartup));
        Raise(nameof(AskToFollowRuleMoves));
        Raise(nameof(ShowTitleBarButton));
        Raise(nameof(ShowDesktopInTray));
        Raise(nameof(LoggingEnabled));
        Raise(nameof(SelectedLogLevel));
        Raise(nameof(DropDownConsoleEnabled));
        Raise(nameof(DropDownConsoleHideWhenFocusLost));
        Raise(nameof(SelectedConsoleShell));
        Raise(nameof(ConsoleShellIsCustom));
        Raise(nameof(ConsoleCustomCommand));
        Raise(nameof(ConsoleWidthPercent));
        Raise(nameof(ConsoleHeightPercent));
        Raise(nameof(ConsoleOpacityPercent));
        Raise(nameof(ConsoleAnimationMilliseconds));
        Raise(nameof(DropDownConsoleFollowMouse));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (LanguageOption option in Languages)
            {
                option.RefreshDisplay();
            }

            // The rows hold text that was already translated, so they are rebuilt.
            RefreshEverything();
            LastAction = string.Empty;
        }));

    private void RefreshCapabilities()
    {
        Capabilities.Clear();

        foreach ((string key, bool available) in _host.VirtualDesktops.Capabilities.Describe())
        {
            Capabilities.Add(new CapabilityRow(key, available));
        }
    }

    private void RefreshHotkeys()
    {
        Hotkeys.Clear();

        IReadOnlyList<HotkeyRegistration> registrations = _host.Hotkeys.Registrations;

        // The list comes from the file, not from what Windows accepted, so a shortcut that could
        // not be registered is still there to be edited. Por grupos y, dentro de cada uno, en el
        // orden del fichero (OrderBy no cambia el orden de los que empatan).
        IEnumerable<HotkeyRow> rows = _host.Configuration.Hotkeys.Select(definition => new HotkeyRow(
            definition,
            registrations.FirstOrDefault(r => string.Equals(r.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase))));

        foreach (HotkeyRow row in rows.OrderBy(r => r.GroupOrder))
        {
            Hotkeys.Add(row);
        }
    }

    private void RefreshRules()
    {
        Rules.Clear();

        foreach (WindowRule rule in _host.Configuration.Rules)
        {
            Rules.Add(new RuleRow(rule, OnRuleEnabledChanged));
        }
    }

    private void RefreshIssues()
    {
        Issues.Clear();

        foreach (ConfigurationIssue issue in _host.Configuration.Issues)
        {
            Issues.Add(new IssueRow(issue.File, issue.Message));
        }
    }

    /// <summary>Reads the desktops again, discarding any name being edited.</summary>
    public void RefreshDesktops()
    {
        Desktops.Clear();

        if (!_host.VirtualDesktops.Capabilities.CanEnumerateDesktops)
        {
            return;
        }

        Guid? actual = _host.VirtualDesktops.GetCurrentDesktop()?.Id;

        foreach (VirtualDesktopInfo desktop in _host.VirtualDesktops.GetDesktops())
        {
            Desktops.Add(new DesktopRow(desktop.Id, desktop.Number, desktop.Name) { IsCurrent = desktop.Id == actual });
        }

        Raise(nameof(CurrentDesktopText));
        Raise(nameof(DesktopCountText));
    }

    /// <summary>
    /// Renames whatever changed and stores the result as the layout to restore.
    /// </summary>
    /// <remarks>
    /// Saving the layout here is the point of the whole screen: renaming a desktop that Windows
    /// later loses would be useless if the name went with it.
    /// </remarks>
    private void SaveDesktopNames()
    {
        int renamed = 0;

        foreach (DesktopRow row in Desktops)
        {
            string wanted = (row.Name ?? string.Empty).Trim();

            if (string.Equals(wanted, row.OriginalName, StringComparison.Ordinal))
            {
                continue;
            }

            OperationResult result = _host.VirtualDesktops.SetDesktopName(row.Id, wanted);

            if (result.IsSuccess)
            {
                renamed++;
            }
            else
            {
                LastAction = result.Error ?? Loc.Get("Common.Failed");
                return;
            }
        }

        RefreshDesktops();
        _host.DesktopLayout.Remember();

        LastAction = Loc.Format("Desktops.Renamed", renamed);
    }

    /// <summary>
    /// Vuelve a leer la ventana que está delante para el apartado «Ventana activa» del panel. Se
    /// llama sola cada vez que cambia.
    /// </summary>
    public void RefreshActiveWindow()
    {
        nint hwnd = _host.WindowManager.GetForegroundWindow();
        WindowInfo? info = _host.WindowManager.GetWindowInfo(hwnd);

        if (info is null)
        {
            ActiveProcess = "-";
            ActiveTitle = "-";
            ActiveHandle = "-";
            ActiveDesktopId = "-";
            ActiveAlwaysOnTop = "-";
            ActivePinned = Loc.Get("Common.Unsupported");
            return;
        }

        ActiveProcess = info.ProcessName.Length > 0 ? info.ProcessName : Loc.Get("Common.Unknown");
        ActiveTitle = info.Title;
        ActiveHandle = info.HandleText;
        ActiveAlwaysOnTop = UiText.YesNo(info.IsTopMost);

        ActiveDesktopId = _host.VirtualDesktops.Capabilities.CanGetWindowDesktop
            ? _host.VirtualDesktops.GetWindowDesktopId(hwnd)?.ToString() ?? Loc.Get("Common.Unknown")
            : Loc.Get("Common.Unsupported");

        ActivePinned = _host.VirtualDesktops.Capabilities.CanPinWindow
            ? UiText.YesNo(false)
            : Loc.Get("Common.Unsupported");
    }

    private void OnForegroundChanged(object? sender, WindowDetectedEventArgs e) =>
        _dispatcher.BeginInvoke(new Action(RefreshActiveWindow));

    private void OnRuleEnabledChanged(RuleRow row)
    {
        _host.Configuration.SaveRules(Rules.Select(r => r.Rule));
        _host.RuleEngine.SetRules(_host.Configuration.Rules);
        LastAction = Loc.Format(row.Enabled ? "Rules.ToastEnabled" : "Rules.ToastDisabled", row.Name);
        Raise(nameof(AutomationsSummary));
    }

    private async void ToggleAlwaysOnTop()
    {
        OperationResult result = await _host
            .ExecuteAsync(CommandType.ToggleAlwaysOnTop, CommandSource.UserInterface)
            .ConfigureAwait(true);

        LastAction = UiText.Describe(result);
        RefreshActiveWindow();
    }

    /// <summary>
    /// Saves an integer setting of the drop down console and applies it straight away.
    /// </summary>
    /// <remarks>
    /// Clamped through <see cref="DropDownConsoleSettings.Normalise"/> before saving, so a slider
    /// dragged to an end and a number typed by hand into settings.json both land in a range that
    /// can actually be drawn.
    /// </remarks>
    private void SetConsoleSetting(
        Action<int> assign,
        int current,
        int value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (current == value)
        {
            return;
        }

        assign(value);
        _host.Settings.DropDownConsole.Normalise();
        _host.Configuration.SaveSettings();
        Raise(propertyName);
    }

    private void SetSetting(
        Action<bool> assign,
        bool current,
        bool value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (current == value)
        {
            return;
        }

        assign(value);
        _host.Configuration.SaveSettings();
        Raise(propertyName);
    }

    // ------------------------------------------------------------------ copias de seguridad

    /// <summary>Abre la ventana de restaurar. La pone la vista, que es quien tiene ventanas.</summary>
    public Func<BackupViewModel, BackupOutcome?>? RestoreBackupRequested { get; set; }

    /// <summary>Pide un sitio donde exportar. Devuelve la ruta, o null si se cancela.</summary>
    public Func<string, string?>? ChooseExportFileRequested { get; set; }

    /// <summary>Pide un fichero que importar, ya confirmado. Null si se cancela.</summary>
    public Func<string?>? ChooseImportFileRequested { get; set; }

    /// <summary>
    /// Las opciones del desplegable «Carpeta de nube»: «(ninguna)» y las de OneDrive y Google
    /// Drive encontradas en este equipo.
    /// </summary>
    public ObservableCollection<CloudFolderOption> CloudFolders { get; } = new();

    /// <summary>
    /// Se encontró alguna carpeta de nube: se enseñan el desplegable y su casilla; si no, el aviso
    /// de que no hay ninguna.
    /// </summary>
    public bool HasCloudFolders => CloudFolders.Count > 1;

    /// <summary>La casilla «Guardar cómo estaba todo antes de cada cambio». Se guarda al cambiarla.</summary>
    public bool BackupAutomaticHistory
    {
        get => _host.Settings.Backup.AutomaticHistory;
        set => SetSetting(v => _host.Settings.Backup.AutomaticHistory = v, _host.Settings.Backup.AutomaticHistory, value);
    }

    /// <summary>
    /// El desplegable «Carpeta de nube». Se guarda al cambiarlo; elegir «(ninguna)» desmarca
    /// también la copia a la nube.
    /// </summary>
    public CloudFolderOption SelectedCloudFolder
    {
        get => _selectedCloudFolder;
        set
        {
            if (value is null || !Set(ref _selectedCloudFolder, value))
            {
                return;
            }

            _host.Settings.Backup.CloudFolder = value.Root;

            // Sin carpeta no hay dónde copiar, y dejar la casilla marcada sería prometer algo que
            // no va a pasar.
            if (value.Folder is null)
            {
                _host.Settings.Backup.CopyToCloud = false;
            }

            _host.Configuration.SaveSettings();
            Raise(nameof(HasCloudFolder));
            Raise(nameof(BackupCopyToCloud));
        }
    }

    /// <summary>Hay una carpeta de nube elegida: sólo entonces se puede marcar la copia a la nube.</summary>
    public bool HasCloudFolder => _selectedCloudFolder.Folder is not null;

    /// <summary>
    /// La casilla «Dejar también una copia ahí cada vez que cambie la configuración». Se guarda al
    /// cambiarla.
    /// </summary>
    public bool BackupCopyToCloud
    {
        get => _host.Settings.Backup.CopyToCloud;
        set => SetSetting(v => _host.Settings.Backup.CopyToCloud = v, _host.Settings.Backup.CopyToCloud, value);
    }

    private void BuildCloudFolders()
    {
        CloudFolders.Clear();
        CloudFolders.Add(CloudFolderOption.None());

        foreach (CloudFolder folder in _host.Backup.CloudFolders)
        {
            CloudFolders.Add(CloudFolderOption.For(folder));
        }

        string configured = _host.Settings.Backup.CloudFolder;

        _selectedCloudFolder = CloudFolders.FirstOrDefault(
                                   option => option.Folder is not null
                                             && string.Equals(option.Root, configured, StringComparison.OrdinalIgnoreCase))
                               ?? CloudFolders[0];

        Raise(nameof(HasCloudFolders));
        Raise(nameof(SelectedCloudFolder));
        Raise(nameof(HasCloudFolder));
    }

    private void BackupNow() => LastAction = _host.Backup.BackupNow().Message;

    private void RestoreBackup()
    {
        if (RestoreBackupRequested is null)
        {
            return;
        }

        BackupOutcome? outcome = RestoreBackupRequested(new BackupViewModel(_host.Backup));

        if (outcome is null)
        {
            return;
        }

        LastAction = outcome.Message;

        if (outcome.Ok)
        {
            // Los ficheros de disco son otros: hay que releerlos y volver a registrar los atajos,
            // o la aplicación seguiría funcionando con la configuración que el usuario acaba de
            // sustituir.
            _host.ReloadConfiguration();
        }
    }

    private void ExportBackup()
    {
        if (ChooseExportFileRequested is null)
        {
            return;
        }

        string suggested = "VirtualDeskManager-" + DateTime.Now.ToString("yyyy-MM-dd") + ConfigurationArchive.Extension;
        string? destination = ChooseExportFileRequested(suggested);

        if (!string.IsNullOrWhiteSpace(destination))
        {
            LastAction = _host.Backup.ExportTo(destination).Message;
        }
    }

    private void ImportBackup()
    {
        if (ChooseImportFileRequested is null)
        {
            return;
        }

        string? source = ChooseImportFileRequested();

        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        BackupOutcome outcome = _host.Backup.RestoreFrom(source);
        LastAction = outcome.Message;

        if (outcome.Ok)
        {
            _host.ReloadConfiguration();
        }
    }

    // ------------------------------------------------------------------ botón de la barra de título

    /// <summary>
    /// Colores de partida, para no obligar a nadie a saberse un hexadecimal.
    /// </summary>
    /// <remarks>
    /// Una lista corta y con la caja de texto al lado, no un selector de color completo. Casi todo
    /// el mundo quiere uno de estos siete; quien quiera otro escribe el suyo, y eso son dos
    /// controles en vez de una rueda de color entera con su ventana emergente.
    /// </remarks>
    public ObservableCollection<ColourPreset> ColourPresets { get; } = new(ColourPreset.All());

    private ColourPreset? _selectedColourPreset;

    /// <summary>
    /// El color de partida elegido en el desplegable; elegirlo escribe su valor en
    /// <see cref="TitleBarButtonColour"/>. Vacío cuando el color guardado no es ninguno de ellos.
    /// </summary>
    public ColourPreset? SelectedColourPreset
    {
        get => _selectedColourPreset;
        set
        {
            if (!Set(ref _selectedColourPreset, value) || value is null)
            {
                return;
            }

            TitleBarButtonColour = value.Hex;
        }
    }

    // ---------------------------------------------------------------- distintivos de escritorio

    /// <summary>
    /// La casilla «Marcar en qué escritorio está cada aplicación». Al cambiarla se guarda y los
    /// distintivos aparecen o desaparecen en el acto.
    /// </summary>
    public bool TaskbarBadgesEnabled
    {
        get => _host.Settings.TaskbarBadges.Enabled;
        set
        {
            SetSetting(v => _host.Settings.TaskbarBadges.Enabled = v, _host.Settings.TaskbarBadges.Enabled, value);
            _host.TaskbarBadges.Apply();
        }
    }

    /// <summary>La casilla «No marcar lo que ya está en este escritorio». Se guarda al cambiarla.</summary>
    public bool TaskbarBadgesHideWhenCurrent
    {
        get => _host.Settings.TaskbarBadges.HideWhenCurrent;
        set => SetSetting(
            v => _host.Settings.TaskbarBadges.HideWhenCurrent = v,
            _host.Settings.TaskbarBadges.HideWhenCurrent,
            value);
    }

    /// <summary>El deslizador «Tamaño» de los distintivos, en porcentaje. Se guarda al cambiarlo.</summary>
    public int TaskbarBadgesSizePercent
    {
        get => _host.Settings.TaskbarBadges.SizePercent;
        set => SetBadgeSetting(
            v => _host.Settings.TaskbarBadges.SizePercent = v,
            _host.Settings.TaskbarBadges.SizePercent,
            value);
    }

    /// <summary>El deslizador «Opacidad» de los distintivos, en porcentaje. Se guarda al cambiarlo.</summary>
    public int TaskbarBadgesOpacityPercent
    {
        get => _host.Settings.TaskbarBadges.OpacityPercent;
        set => SetBadgeSetting(
            v => _host.Settings.TaskbarBadges.OpacityPercent = v,
            _host.Settings.TaskbarBadges.OpacityPercent,
            value);
    }

    /// <summary>
    /// El cuadro «Color» de los distintivos, en <c>#RRGGBB</c>. Se guarda al salir del cuadro; uno
    /// que no se entiende vuelve al azul de serie.
    /// </summary>
    public string TaskbarBadgesColour
    {
        get => _host.Settings.TaskbarBadges.Colour;
        set
        {
            if (string.Equals(_host.Settings.TaskbarBadges.Colour, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _host.Settings.TaskbarBadges.Colour = value;
            _host.Settings.TaskbarBadges.Normalise();
            _host.Configuration.SaveSettings();

            Raise(nameof(TaskbarBadgesColour));
        }
    }

    /// <summary>La barra de Windows esconde lo que está en otros escritorios.</summary>
    /// <remarks>
    /// Se enseña en los ajustes porque con ese filtro puesto —el de fábrica— una aplicación
    /// abierta en otro escritorio ni siquiera tiene botón que marcar, y la función parecería rota
    /// cuando lo que pasa es que Windows no enseña ese botón.
    /// </remarks>
    public bool TaskbarShowsOnlyCurrentDesktop =>
        VirtualDeskManager.Windows.Taskbar.TaskbarFilter.ShowsOnlyCurrentDesktop();

    private void SetBadgeSetting(
        Action<int> assign,
        int current,
        int value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (current == value)
        {
            return;
        }

        assign(value);
        _host.Settings.TaskbarBadges.Normalise();
        _host.Configuration.SaveSettings();

        Raise(propertyName);
    }

    /// <summary>
    /// El deslizador «Tamaño» del botón de la barra de título, en porcentaje. Al cambiarlo se
    /// guarda y el botón se repinta en el acto.
    /// </summary>
    public int TitleBarButtonSizePercent
    {
        get => _host.Settings.TitleBarButton.SizePercent;
        set => SetButtonSetting(v => _host.Settings.TitleBarButton.SizePercent = v, _host.Settings.TitleBarButton.SizePercent, value);
    }

    /// <summary>
    /// El deslizador «Opacidad» del botón de la barra de título, en porcentaje. Al cambiarlo se
    /// guarda y el botón se repinta en el acto.
    /// </summary>
    public int TitleBarButtonOpacityPercent
    {
        get => _host.Settings.TitleBarButton.OpacityPercent;
        set => SetButtonSetting(v => _host.Settings.TitleBarButton.OpacityPercent = v, _host.Settings.TitleBarButton.OpacityPercent, value);
    }

    /// <summary>
    /// El deslizador «Correrlo a la izquierda», en píxeles: para que el botón no tape lo que algunas
    /// aplicaciones dibujan en su barra de título. Al cambiarlo se guarda y se aplica en el acto.
    /// </summary>
    public int TitleBarButtonOffset
    {
        get => _host.Settings.TitleBarButtonOffset;
        set => SetButtonSetting(v => _host.Settings.TitleBarButtonOffset = v, _host.Settings.TitleBarButtonOffset, value);
    }

    /// <summary>
    /// El cuadro «Color» del botón de la barra de título, en <c>#RRGGBB</c>. Un color que no se
    /// entiende no se guarda y el cuadro vuelve al anterior; uno válido se guarda y se aplica en el
    /// acto.
    /// </summary>
    public string TitleBarButtonColour
    {
        get => _host.Settings.TitleBarButton.Colour;
        set
        {
            if (string.Equals(_host.Settings.TitleBarButton.Colour, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Lo que no se entienda no se guarda: si el usuario deja la caja a medias, el botón se
            // queda con el color que ya tenía en vez de irse al gris de golpe.
            if (!RgbColour.TryParse(value, out RgbColour parsed))
            {
                Raise();
                return;
            }

            _host.Settings.TitleBarButton.Colour = parsed.Text;
            _host.Configuration.SaveSettings();
            _host.ApplyTitleBarButtonSettings();

            Raise();
            Raise(nameof(TitleBarButtonPreview));
            MatchColourPreset();
        }
    }

    /// <summary>El color con su transparencia, tal y como se va a ver sobre la barra de título.</summary>
    public Brush TitleBarButtonPreview
    {
        get
        {
            TitleBarButtonSettings style = _host.Settings.TitleBarButton;
            RgbColour colour = style.ParsedColour;
            byte alpha = (byte)Math.Clamp((style.OpacityPercent * 255) / 100, 0, 255);

            SolidColorBrush brush = new(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Deja marcado en la lista el color de partida que coincida, si es alguno.</summary>
    private void MatchColourPreset()
    {
        string current = _host.Settings.TitleBarButton.ParsedColour.Text;

        ColourPreset? match = ColourPresets.FirstOrDefault(
            preset => string.Equals(preset.Hex, current, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(_selectedColourPreset, match))
        {
            _selectedColourPreset = match;
            Raise(nameof(SelectedColourPreset));
        }
    }

    /// <summary>
    /// Guarda y se lo lleva al botón en el acto, sin reiniciar.
    /// </summary>
    /// <remarks>
    /// Ajustar un color o un tamaño a ciegas y tener que cerrar para verlo no es ajustar, es
    /// adivinar. El botón se repinta en cuanto se suelta el deslizador.
    /// </remarks>
    private void SetButtonSetting(
        Action<int> assign,
        int current,
        int value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (current == value)
        {
            return;
        }

        assign(value);
        _host.Settings.TitleBarButton.Normalise();
        _host.Configuration.SaveSettings();
        _host.ApplyTitleBarButtonSettings();

        Raise(propertyName);
        Raise(nameof(TitleBarButtonPreview));
    }

    // ------------------------------------------------------------------ posicion de las ventanas

    /// <summary>
    /// La casilla «Quedarse con las combinaciones que Windows se reserva». Al cambiarla se guarda y
    /// se vuelven a registrar los atajos: los que Windows rechace pasan al gancho de teclado, o
    /// dejan de pasar.
    /// </summary>
    public bool InterceptReservedHotkeys
    {
        get => _host.Settings.InterceptReservedHotkeys;
        set
        {
            SetSetting(
                v => _host.Settings.InterceptReservedHotkeys = v,
                _host.Settings.InterceptReservedHotkeys,
                value);

            // Vuelve a registrar: lo que Windows rechace pasa al gancho, o deja de pasar.
            _host.ApplyHotkeys(_host.Configuration.Hotkeys);
        }
    }

    /// <summary>La casilla «Recordar dónde está cada ventana». Se guarda al cambiarla.</summary>
    public bool WindowLayoutEnabled
    {
        get => _host.Settings.WindowLayout.Enabled;
        set => SetSetting(v => _host.Settings.WindowLayout.Enabled = v, _host.Settings.WindowLayout.Enabled, value);
    }

    /// <summary>La casilla «Devolverlas a su sitio al volver las pantallas o del reposo». Se guarda al cambiarla.</summary>
    public bool WindowLayoutRestoreAutomatically
    {
        get => _host.Settings.WindowLayout.RestoreAutomatically;
        set => SetSetting(
            v => _host.Settings.WindowLayout.RestoreAutomatically = v,
            _host.Settings.WindowLayout.RestoreAutomatically,
            value);
    }

    /// <summary>La casilla «Devolverlas también al arrancar la aplicación». Se guarda al cambiarla.</summary>
    public bool WindowLayoutRestoreOnStartup
    {
        get => _host.Settings.WindowLayout.RestoreOnStartup;
        set => SetSetting(
            v => _host.Settings.WindowLayout.RestoreOnStartup = v,
            _host.Settings.WindowLayout.RestoreOnStartup,
            value);
    }

    /// <summary>
    /// El deslizador «Esperar antes de recolocar», en milisegundos: lo que se espera antes de
    /// recolocar las ventanas. Se guarda al cambiarlo.
    /// </summary>
    public int WindowLayoutDelayMilliseconds
    {
        get => _host.Settings.WindowLayout.RestoreDelayMilliseconds;
        set
        {
            if (_host.Settings.WindowLayout.RestoreDelayMilliseconds == value)
            {
                return;
            }

            _host.Settings.WindowLayout.RestoreDelayMilliseconds = value;
            _host.Settings.WindowLayout.Normalise();
            _host.Configuration.SaveSettings();
            Raise();
        }
    }

    /// <summary>Cuantos conjuntos de pantallas hay recordados, y si el de ahora es uno de ellos.</summary>
    public string WindowLayoutSummary =>
        Loc.Format("Settings.WindowLayoutSaved", _host.WindowLayout.SavedLayoutCount)
        + (_host.WindowLayout.HasLayoutForCurrentDisplays()
            ? ""
            : "   " + Loc.Get("Settings.WindowLayoutNoneForThese"));

    private void RestoreLayoutNow()
    {
        int moved = _host.WindowLayout.Restore();

        LastAction = moved < 0
            ? Loc.Get("Settings.WindowLayoutNoneForThese")
            : Loc.Format("Settings.WindowLayoutSaved", _host.WindowLayout.SavedLayoutCount);

        Raise(nameof(WindowLayoutSummary));
    }

    private void ForgetLayouts()
    {
        _host.WindowLayout.Forget();
        Raise(nameof(WindowLayoutSummary));
    }

    // ------------------------------------------------------------------ zonas

    /// <summary>Un reparto por cada monitor conectado ahora, en la pestaña de zonas.</summary>
    public ObservableCollection<ZoneMonitorRow> ZoneMonitors { get; } = new();

    /// <summary>La casilla «Usar zonas». Al cambiarla se guarda y se aplica en el acto.</summary>
    public bool ZonesEnabled
    {
        get => _host.Settings.Zones.Enabled;
        set
        {
            SetSetting(v => _host.Settings.Zones.Enabled = v, _host.Settings.Zones.Enabled, value);
            _host.ZoneSnap.Apply();
        }
    }

    /// <summary>
    /// La casilla «Que las ventanas encajadas queden pegadas»: les quita las esquinas redondeadas y
    /// el filo de Windows 11. Al cambiarla se guarda y se aplica en el acto.
    /// </summary>
    public bool ZonesSeamlessEdges
    {
        get => _host.Settings.Zones.SeamlessEdges;
        set
        {
            SetSetting(v => _host.Settings.Zones.SeamlessEdges = v, _host.Settings.Zones.SeamlessEdges, value);
            _host.ZoneSnap.Apply();
        }
    }

    /// <summary>
    /// La casilla «Encajar en una zona al arrastrar con Mayús». Al cambiarla se guarda y se aplica
    /// en el acto.
    /// </summary>
    public bool ZonesSnapWhileDragging
    {
        get => _host.Settings.Zones.SnapWhileDragging;
        set
        {
            SetSetting(
                v => _host.Settings.Zones.SnapWhileDragging = v,
                _host.Settings.Zones.SnapWhileDragging,
                value);

            // Sin reiniciar: el gancho de arrastre se pone o se quita en el acto, que es lo que
            // permite probar la casilla y ver el efecto arrastrando una ventana ahi mismo.
            _host.ZoneSnap.Apply();
        }
    }

    /// <summary>
    /// Arma una fila por monitor de verdad, no por lo que ponga el fichero.
    /// </summary>
    /// <remarks>
    /// Se parte de los monitores que hay conectados ahora: un reparto guardado para un monitor que
    /// ya no existe no se puede editar ni tiene sentido enseñarlo, y se queda en el fichero por si
    /// vuelve a conectarse.
    /// </remarks>
    public void RefreshZoneMonitors()
    {
        ZoneMonitors.Clear();

        foreach (MonitorInfo monitor in _host.WindowManager.GetMonitors())
        {
            ZoneMonitors.Add(new ZoneMonitorRow(
                monitor,
                _host.Settings.Zones.For(monitor.Number),
                SaveZones));
        }
    }

    private void SaveZones()
    {
        _host.Settings.Zones.Normalise();
        _host.Configuration.SaveSettings();
    }

    /// <summary>
    /// Crea un atajo por zona, tantos como zonas tenga el monitor mejor repartido.
    /// </summary>
    /// <remarks>
    /// A peticion y no de serie. Los atajos de zona ocupan una combinacion cada uno, y anadir
    /// nueve a quien no va a usar las zonas es quitarle nueve combinaciones de su teclado sin
    /// haberselo preguntado.
    /// </remarks>
    private void AddZoneHotkeys()
    {
        int zones = _host.Settings.Zones.MaximumZoneCount;

        if (zones <= 1)
        {
            LastAction = Loc.Get("Zones.NoZones");
            return;
        }

        List<HotkeyDefinition> all = _host.Configuration.Hotkeys.ToList();
        int added = 0;

        for (int zone = 1; zone <= zones; zone++)
        {
            string id = "zone-" + zone;

            if (all.Any(hotkey => string.Equals(hotkey.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            HotkeyDefinition definition = new()
            {
                Id = id,
                // Win+Alt+numero: es lo que usa FancyZones, asi que a quien venga de ahi le suena,
                // y no choca con ninguno de los atajos que ya trae esta aplicacion.
                Modifiers = HotkeyModifiers.Windows | HotkeyModifiers.Alt,
                Key = HotkeyKey.D1 + (zone - 1),
                Command = new CommandDefinition(CommandType.MoveWindowToZone)
            };

            definition.Command.Parameters["zone"] = zone.ToString(CultureInfo.InvariantCulture);

            all.Add(definition);
            added++;
        }

        if (added == 0)
        {
            LastAction = Loc.Format("Zones.HotkeysAdded", 0);
            return;
        }

        _host.Settings.Zones.HotkeysAdded = true;
        _host.Configuration.SaveSettings();
        _host.ApplyHotkeys(all);

        LastAction = Loc.Format("Zones.HotkeysAdded", added);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LastAction = ex.Message;
        }
    }
}
