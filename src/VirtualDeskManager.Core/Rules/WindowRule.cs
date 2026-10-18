using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>An automatic rule (spec section 21). Serialized in rules.json.</summary>
public sealed class WindowRule
{
    /// <summary>Identificador estable de la regla, para reconocerla aunque se renombre.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nombre que pone el usuario. Es el que sale en el diario como la regla que decidió algo.</summary>
    public string Name { get; set; } = "";

    /// <summary>Si está apagada, el motor la salta como si no existiera.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Higher number wins when two rules set the same property (spec section 22).</summary>
    public int Priority { get; set; }

    /// <summary>A qué ventanas se aplica.</summary>
    public WindowMatcher Match { get; set; } = new();

    /// <summary>Qué les hace.</summary>
    public WindowActions Actions { get; set; } = new();

    /// <summary>Nombre y prioridad, en una línea.</summary>
    public override string ToString() => $"{Name} (priority {Priority})";
}

/// <summary>Conditions of a rule. All the criteria that are set must match (logical AND).</summary>
public sealed class WindowMatcher
{
    /// <summary>Nombre del proceso, con o sin <c>.exe</c> y sin distinguir mayúsculas.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Ruta completa del ejecutable, sin distinguir mayúsculas. Admite los comodines <c>*</c> y <c>?</c>.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Texto que tiene que aparecer en el título, sin distinguir mayúsculas.</summary>
    public string? TitleContains { get; set; }

    /// <summary>
    /// Expresión regular sobre el título, sin distinguir mayúsculas. Si no compila, la regla no
    /// casa nunca y aparece en <see cref="IRuleEngine.InvalidRules"/>.
    /// </summary>
    public string? TitleRegex { get; set; }

    /// <summary>Clase de ventana de Win32, exacta salvo mayúsculas.</summary>
    public string? WindowClass { get; set; }

    /// <summary>A matcher without any criterion matches nothing: it would apply to every window.</summary>
    public bool HasAnyCriteria =>
        !string.IsNullOrWhiteSpace(ProcessName)
        || !string.IsNullOrWhiteSpace(ExecutablePath)
        || !string.IsNullOrWhiteSpace(TitleContains)
        || !string.IsNullOrWhiteSpace(TitleRegex)
        || !string.IsNullOrWhiteSpace(WindowClass);

    /// <summary>
    /// Los criterios en una línea legible, en inglés. Es el nombre que se le da al cargar una regla
    /// que no tiene ninguno.
    /// </summary>
    public string Describe()
    {
        List<string> parts = new();

        if (!string.IsNullOrWhiteSpace(ProcessName))
        {
            parts.Add($"process={ProcessName}");
        }

        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            parts.Add($"path={ExecutablePath}");
        }

        if (!string.IsNullOrWhiteSpace(TitleContains))
        {
            parts.Add($"title contains \"{TitleContains}\"");
        }

        if (!string.IsNullOrWhiteSpace(TitleRegex))
        {
            parts.Add($"title matches /{TitleRegex}/");
        }

        if (!string.IsNullOrWhiteSpace(WindowClass))
        {
            parts.Add($"class={WindowClass}");
        }

        return parts.Count == 0 ? "(no criteria)" : string.Join(" and ", parts);
    }
}

/// <summary>What a rule does once it matches. Null means "this rule does not care".</summary>
public sealed class WindowActions
{
    /// <summary>
    /// Escritorio al que mandar la ventana: su nombre, su número desde uno o su identificador
    /// (ver <see cref="DesktopResolver"/>). Nunca cambia al usuario de escritorio.
    /// </summary>
    public string? Desktop { get; set; }

    /// <summary>Poner o quitar «siempre encima».</summary>
    public bool? AlwaysOnTop { get; set; }

    /// <summary>Anclar la ventana a todos los escritorios, o desanclarla.</summary>
    public bool? PinWindow { get; set; }

    /// <summary>Anclar a todos los escritorios la aplicación entera, también las ventanas que abra después.</summary>
    public bool? PinApplication { get; set; }

    /// <summary>Monitor al que llevarla, contando desde uno. Con un solo monitor no hace nada.</summary>
    public int? Monitor { get; set; }

    /// <summary>Maximizarla (<c>true</c>) o dejarla en tamaño normal (<c>false</c>).</summary>
    public bool? Maximized { get; set; }

    /// <summary>Posición y tamaño exactos, en píxeles de pantalla.</summary>
    public WindowBounds? Bounds { get; set; }

    /// <summary>
    /// When this application is already open, go to the window it already has instead of
    /// keeping the one that has just opened.
    /// </summary>
    /// <remarks>
    /// Per application on purpose, and off by default. Applied to everything it would be a
    /// disaster: a second Explorer window, a second browser window or a second editor window are
    /// all perfectly ordinary things to want, and this would close them the moment they appeared.
    /// It earns its keep on the applications that insist on opening a fresh window every time
    /// their icon is clicked, when what the user meant was "show me the one I already have".
    /// </remarks>
    public bool? ReuseExistingWindow { get; set; }

    /// <summary>
    /// Dejar que la aplicacion abra una ventana nueva aunque ya tenga otra en otro escritorio.
    /// </summary>
    /// <remarks>
    /// Apagada de serie, y con eso basta para el caso que la trajo: pinchar Outlook en la barra
    /// cuando esta abierto en otro escritorio abria una segunda instancia ademas de llevarte
    /// alli, y te quedabas con dos.
    ///
    /// Encendida es una manera legitima de trabajar --dos ventanas del mismo programa, una en
    /// cada escritorio-- y por eso existe en vez de darse por imposible.
    /// </remarks>
    public bool? NewInstance { get; set; }

    /// <summary>Si la regla pide algo; una sin ninguna acción no hace nada aunque case.</summary>
    public bool HasAny =>
        Desktop is not null
        || AlwaysOnTop is not null
        || PinWindow is not null
        || PinApplication is not null
        || Monitor is not null
        || Maximized is not null
        || Bounds is not null
        || ReuseExistingWindow is not null
        || NewInstance is not null;

    /// <summary>
    /// Las acciones en una línea legible, en inglés. No incluye
    /// <see cref="NewInstance"/>.
    /// </summary>
    public string Describe()
    {
        List<string> parts = new();

        if (AlwaysOnTop is not null)
        {
            parts.Add(AlwaysOnTop.Value ? "always on top" : "not always on top");
        }

        if (PinWindow is not null)
        {
            parts.Add(PinWindow.Value ? "pin window" : "unpin window");
        }

        if (PinApplication is not null)
        {
            parts.Add(PinApplication.Value ? "pin application" : "unpin application");
        }

        if (Desktop is not null)
        {
            parts.Add($"desktop \"{Desktop}\"");
        }

        if (Monitor is not null)
        {
            parts.Add($"monitor {Monitor.Value}");
        }

        if (Maximized is not null)
        {
            parts.Add(Maximized.Value ? "maximized" : "restored");
        }

        if (Bounds is not null)
        {
            parts.Add($"bounds {Bounds.Value}");
        }

        if (ReuseExistingWindow is not null)
        {
            parts.Add(ReuseExistingWindow.Value ? "reuse the open window" : "always a new window");
        }

        return parts.Count == 0 ? "(no actions)" : string.Join(", ", parts);
    }
}
