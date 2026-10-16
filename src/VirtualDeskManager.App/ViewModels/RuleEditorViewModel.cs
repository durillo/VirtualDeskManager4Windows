using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>
/// Editor of a single rule (spec section 31).
/// </summary>
/// <remarks>
/// The three actions that can be turned on, off or left alone are exposed as
/// <see cref="bool"/>? and shown as three state check boxes, which is exactly the shape of
/// <see cref="WindowActions"/>: null means "this rule does not care about that".
/// </remarks>
public sealed class RuleEditorViewModel : ObservableObject
{
    private readonly Guid _id;

    private string _name;
    private int _priority;
    private bool _enabled;
    private string _processName;
    private string _executablePath;
    private string _titleContains;
    private string _titleRegex;
    private string _windowClass;
    private bool? _alwaysOnTop;
    private bool? _pinWindow;
    private bool? _maximized;
    private bool? _reuseExistingWindow;
    private string _desktop;
    private string _monitor;
    private string _error = "";

    /// <summary>
    /// Las aplicaciones abiertas ahora mismo, para poder elegir una en vez de escribirla.
    /// </summary>
    public ObservableCollection<RunningApplication> Applications { get; } = new();

    /// <summary>Los escritorios que existen, para no tener que acertar el nombre a ciegas.</summary>
    public ObservableCollection<string> Desktops { get; } = new();

    private readonly List<RunningApplication> _allApplications = new();
    private string _search = "";
    private RunningApplication? _selectedApplication;

    /// <summary>Prepara el editor para una regla nueva o para una que ya existe.</summary>
    /// <param name="existing">La regla que se edita; <c>null</c> para crear una nueva, con prioridad 100.</param>
    /// <param name="applications">Las aplicaciones abiertas, para la lista «Qué aplicación».</param>
    /// <param name="desktops">Los nombres de los escritorios, para el desplegable del escritorio.</param>
    /// <param name="monitors">Los monitores; con uno solo no se ofrece elegir.</param>
    public RuleEditorViewModel(
        WindowRule? existing,
        IReadOnlyList<RunningApplication>? applications = null,
        IReadOnlyList<string>? desktops = null,
        IReadOnlyList<MonitorChoice>? monitors = null)
    {
        IsNew = existing is null;

        if (applications is not null)
        {
            _allApplications.AddRange(applications);
        }

        foreach (string desktop in desktops ?? Array.Empty<string>())
        {
            Desktops.Add(desktop);
        }

        IReadOnlyList<MonitorChoice> available = monitors ?? Array.Empty<MonitorChoice>();

        if (available.Count > 1)
        {
            // Primero la opción de no decir nada, o una vez elegido un monitor no habría forma de
            // volver atrás y la regla se quedaría atada a una pantalla para siempre.
            Monitors.Add(MonitorChoice.None);

            foreach (MonitorChoice monitor in available)
            {
                Monitors.Add(monitor);
            }
        }

        WindowRule source = existing ?? new WindowRule { Priority = 100 };

        _id = source.Id;
        _name = source.Name;
        _priority = source.Priority;
        _enabled = source.Enabled;

        _processName = source.Match.ProcessName ?? "";
        _executablePath = source.Match.ExecutablePath ?? "";
        _titleContains = source.Match.TitleContains ?? "";
        _titleRegex = source.Match.TitleRegex ?? "";
        _windowClass = source.Match.WindowClass ?? "";

        _alwaysOnTop = source.Actions.AlwaysOnTop;
        _pinWindow = source.Actions.PinWindow;
        _maximized = source.Actions.Maximized;
        _reuseExistingWindow = source.Actions.ReuseExistingWindow;
        _desktop = source.Actions.Desktop ?? "";
        _monitor = source.Actions.Monitor?.ToString(CultureInfo.InvariantCulture) ?? "";

        // Estas dos no tienen casilla en el editor, pero sí existen en las reglas: se guardan
        // aquí para devolverlas tal cual al grabar. Sin esto, abrir una regla que las usara y
        // darle a guardar se las llevaba por delante sin decir nada. La de Spotify las usa.
        _pinApplication = source.Actions.PinApplication;
        _newInstance = source.Actions.NewInstance;
        _bounds = source.Actions.Bounds;

        FilterApplications();

        // Al abrir una regla que ya existe, se deja marcada su aplicación en la lista si sigue
        // abierta, para que se vea de un vistazo de cuál es la regla.
        _selectedApplication = _allApplications.FirstOrDefault(
            a => string.Equals(a.ProcessName, _processName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Las tres posturas que puede tomar una regla ante cada propiedad.</summary>
    public IReadOnlyList<RuleActionChoice> ActionChoices { get; } = RuleActionChoice.All;

    /// <summary>Los monitores del equipo, para elegir uno en vez de escribir su número.</summary>
    public ObservableCollection<MonitorChoice> Monitors { get; } = new();

    /// <summary>
    /// Sólo se ofrece elegir monitor cuando hay más de uno.
    /// </summary>
    /// <remarks>
    /// Con un único monitor, el campo no puede decir nada útil y sólo es una pregunta más en un
    /// formulario que ya era largo.
    /// </remarks>
    public bool HasSeveralMonitors => Monitors.Count > 1;

    /// <summary>El desplegable «Siempre visible»; guarda en <see cref="AlwaysOnTop"/>.</summary>
    public RuleActionChoice AlwaysOnTopChoice
    {
        get => RuleActionChoice.For(AlwaysOnTop);
        set => AlwaysOnTop = value?.Value;
    }

    /// <summary>El desplegable «Mostrar en todos los escritorios»; guarda en <see cref="PinWindow"/>.</summary>
    public RuleActionChoice PinWindowChoice
    {
        get => RuleActionChoice.For(PinWindow);
        set => PinWindow = value?.Value;
    }

    /// <summary>El desplegable «Ver la aplicación en todos los escritorios»; guarda en <see cref="PinApplication"/>.</summary>
    public RuleActionChoice PinApplicationChoice
    {
        get => RuleActionChoice.For(PinApplication);
        set => PinApplication = value?.Value;
    }

    /// <summary>
    /// El desplegable «Abrir una ventana nueva aunque ya esté abierta en otro escritorio»; guarda
    /// en <see cref="NewInstance"/>.
    /// </summary>
    public RuleActionChoice NewInstanceChoice
    {
        get => RuleActionChoice.For(NewInstance);
        set => NewInstance = value?.Value;
    }

    /// <summary>El desplegable «Maximizada»; guarda en <see cref="Maximized"/>.</summary>
    public RuleActionChoice MaximizedChoice
    {
        get => RuleActionChoice.For(Maximized);
        set => Maximized = value?.Value;
    }

    /// <summary>
    /// El desplegable «Ir a la ventana que la aplicación ya tiene abierta»; guarda en
    /// <see cref="ReuseExistingWindow"/>.
    /// </summary>
    public RuleActionChoice ReuseExistingWindowChoice
    {
        get => RuleActionChoice.For(ReuseExistingWindow);
        set => ReuseExistingWindow = value?.Value;
    }

    /// <summary>
    /// El desplegable «Monitor», que sólo sale con más de uno. Elegir la opción vacía deja la
    /// regla sin monitor; guarda en <see cref="Monitor"/>.
    /// </summary>
    public MonitorChoice? SelectedMonitor
    {
        get
        {
            if (!int.TryParse(Monitor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                return Monitors.FirstOrDefault(m => m.IsNone);
            }

            return Monitors.FirstOrDefault(m => m.Number == number);
        }
        set
        {
            Monitor = value is null || value.IsNone
                ? ""
                : value.Number.ToString(CultureInfo.InvariantCulture);

            Raise(nameof(SelectedMonitor));
        }
    }

    /// <summary>Lo escrito en el buscador de aplicaciones.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                FilterApplications();
            }
        }
    }

    /// <summary>
    /// La aplicación elegida en la lista.
    /// </summary>
    /// <remarks>
    /// Elegir una rellena el nombre del proceso, que es lo que de verdad usa la regla, y además
    /// bautiza la regla si todavía no tiene nombre: quien está creando una regla para Outlook
    /// quiere que se llame "Microsoft Outlook", no rellenar dos campos.
    /// </remarks>
    public RunningApplication? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (!Set(ref _selectedApplication, value) || value is null)
            {
                return;
            }

            ProcessName = value.ProcessName;

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = value.FriendlyName;
            }
        }
    }

    /// <summary>True cuando no hay ninguna aplicación que enseñar con lo que se ha buscado.</summary>
    public bool HasApplications => Applications.Count > 0;

    private void FilterApplications()
    {
        Applications.Clear();

        foreach (RunningApplication application in _allApplications)
        {
            if (application.Matches(_search))
            {
                Applications.Add(application);
            }
        }

        Raise(nameof(HasApplications));
    }

    private bool? _pinApplication;

    private bool? _newInstance;
    private WindowBounds? _bounds;

    /// <summary>Se está creando una regla, no editando una que ya existía.</summary>
    public bool IsNew { get; }

    /// <summary>El título de la ventana: «Nueva regla» o «Editar regla».</summary>
    public string Title => Loc.Get(IsNew ? "RuleEditor.TitleNew" : "RuleEditor.TitleEdit");

    /// <summary>El cuadro «Nombre».</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>
    /// El cuadro «Prioridad». Si dos reglas que encajan con la misma ventana dicen cosas
    /// distintas de la misma propiedad, gana la de número más alto.
    /// </summary>
    public int Priority
    {
        get => _priority;
        set => Set(ref _priority, value);
    }

    /// <summary>La casilla «Activa»: una regla apagada se guarda, pero no se aplica.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    /// <summary>
    /// El cuadro «Nombre del proceso», con extensión: <c>olk.exe</c>. Se rellena solo al elegir
    /// una aplicación de la lista. Vacío es «cualquiera».
    /// </summary>
    public string ProcessName
    {
        get => _processName;
        set => Set(ref _processName, value);
    }

    /// <summary>El cuadro «Ruta del ejecutable». Vacío es «cualquiera».</summary>
    public string ExecutablePath
    {
        get => _executablePath;
        set => Set(ref _executablePath, value);
    }

    /// <summary>El cuadro «El título contiene». Vacío es «cualquiera».</summary>
    public string TitleContains
    {
        get => _titleContains;
        set => Set(ref _titleContains, value);
    }

    /// <summary>El cuadro «El título coincide (expresión regular)». Vacío es «cualquiera».</summary>
    public string TitleRegex
    {
        get => _titleRegex;
        set => Set(ref _titleRegex, value);
    }

    /// <summary>El cuadro «Clase de ventana». Vacío es «cualquiera».</summary>
    public string WindowClass
    {
        get => _windowClass;
        set => Set(ref _windowClass, value);
    }

    /// <summary>Poner la ventana siempre visible, quitárselo o no tocarlo (<c>null</c>).</summary>
    public bool? AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => Set(ref _alwaysOnTop, value);
    }

    /// <summary>
    /// Anclar la ventana para que se vea en todos los escritorios, desanclarla o no tocarlo
    /// (<c>null</c>).
    /// </summary>
    public bool? PinWindow
    {
        get => _pinWindow;
        set => Set(ref _pinWindow, value);
    }

    /// <summary>
    /// Anclar la aplicacion entera, y no solo la ventana que la regla haya pillado.
    /// </summary>
    /// <remarks>
    /// Windows se acuerda de una aplicacion anclada y se lo aplica a las ventanas que abra
    /// despues; anclar una ventana alcanza solo a esa. Es la diferencia entre que una
    /// videollamada de Teams herede el ajuste o nazca sin el.
    /// </remarks>
    public bool? PinApplication
    {
        get => _pinApplication;
        set => Set(ref _pinApplication, value);
    }

    /// <summary>
    /// Dejar que se abra una ventana nueva aunque la aplicacion ya tenga otra en otro escritorio.
    /// </summary>
    /// <remarks>
    /// Sin poner nada se pregunta, que es lo de serie. Esto es para dejar de preguntar en el
    /// sentido contrario a "ir": quedarse aqui con la ventana recien abierta.
    /// </remarks>
    public bool? NewInstance
    {
        get => _newInstance;
        set => Set(ref _newInstance, value);
    }

    /// <summary>
    /// Ir a la ventana que la aplicación ya tiene abierta en vez de quedarse con la nueva.
    /// </summary>
    /// <remarks>
    /// De tres estados como las demás: sin marcar es "no opino", y así una regla sin esta acción
    /// no se la impone a nadie. Marcarla cierra la ventana recién abierta, de modo que sólo tiene
    /// sentido en aplicaciones que insisten en abrir ventana nueva cada vez que pulsas su icono.
    /// </remarks>
    public bool? ReuseExistingWindow
    {
        get => _reuseExistingWindow;
        set => Set(ref _reuseExistingWindow, value);
    }

    /// <summary>Maximizar la ventana, devolverla a su tamaño o no tocarlo (<c>null</c>).</summary>
    public bool? Maximized
    {
        get => _maximized;
        set => Set(ref _maximized, value);
    }

    /// <summary>
    /// El «Nombre del escritorio» al que se manda la ventana; se puede elegir de la lista o
    /// escribir. Vacío es no moverla.
    /// </summary>
    public string Desktop
    {
        get => _desktop;
        set => Set(ref _desktop, value);
    }

    /// <summary>
    /// El número del monitor al que se manda la ventana, empezando en 1, como texto. Vacío es no
    /// moverla. En pantalla se elige con <see cref="SelectedMonitor"/>.
    /// </summary>
    public string Monitor
    {
        get => _monitor;
        set => Set(ref _monitor, value);
    }

    /// <summary>Por qué la regla no se puede guardar todavía; vacío si no hay nada.</summary>
    public string Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>
    /// Arma la regla con lo que hay en el formulario y la valida. Las acciones que el editor no
    /// enseña (como la posición guardada) se conservan tal cual venían.
    /// </summary>
    /// <returns>La regla, o <c>null</c> si algo no vale; entonces <see cref="Error"/> dice qué.</returns>
    public WindowRule? TryBuild()
    {
        WindowRule rule = new()
        {
            Id = _id == Guid.Empty ? Guid.NewGuid() : _id,
            Name = Name.Trim(),
            Priority = Priority,
            Enabled = Enabled,
            Match = new WindowMatcher
            {
                ProcessName = Clean(ProcessName),
                ExecutablePath = Clean(ExecutablePath),
                TitleContains = Clean(TitleContains),
                TitleRegex = Clean(TitleRegex),
                WindowClass = Clean(WindowClass)
            },
            Actions = new WindowActions
            {
                AlwaysOnTop = AlwaysOnTop,
                PinWindow = PinWindow,
                PinApplication = _pinApplication,
                NewInstance = _newInstance,
                Bounds = _bounds,
                Maximized = Maximized,
                ReuseExistingWindow = ReuseExistingWindow,
                Desktop = Clean(Desktop),
                Monitor = int.TryParse(Monitor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int monitor)
                    ? monitor
                    : null
            }
        };

        IReadOnlyList<ValidationIssue> issues = ConfigurationValidator.Validate(rule);

        if (issues.Count > 0)
        {
            Error = string.Join(" ", issues.Select(i => Loc.Get("Validation." + i)));
            return null;
        }

        Error = "";
        return rule;
    }

    /// <summary>An empty box means "no criterion", not an empty string to match against.</summary>
    private static string? Clean(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
