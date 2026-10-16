namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Every user visible action of the application (spec section 7). Hotkeys, tray menu, UI and
/// rules all go through these, never through a concrete implementation.
/// </summary>
public enum CommandType
{
    /// <summary>Unknown or missing command. Always rejected by the dispatcher.</summary>
    None = 0,

    /// <summary>Pasa al escritorio de la izquierda.</summary>
    SwitchDesktopLeft,
    /// <summary>Pasa al escritorio de la derecha.</summary>
    SwitchDesktopRight,

    /// <summary>Salta a un escritorio concreto, dicho por parámetro (<c>desktop</c>, <c>number</c>, <c>index</c> o <c>name</c>).</summary>
    SwitchToDesktop,

    /// <summary>Lleva la ventana al escritorio de la izquierda del suyo.</summary>
    MoveWindowLeft,
    /// <summary>Lleva la ventana al escritorio de la derecha del suyo.</summary>
    MoveWindowRight,
    /// <summary>Lleva la ventana a un escritorio concreto, dicho por parámetro como en <see cref="SwitchToDesktop"/>.</summary>
    MoveWindowToDesktop,

    /// <summary>Pone o quita la ventana de «siempre encima». El parámetro <c>enabled</c> fuerza un estado.</summary>
    ToggleAlwaysOnTop,

    /// <summary>Muestra la ventana en todos los escritorios, o deja de hacerlo.</summary>
    TogglePinWindow,
    /// <summary>
    /// Muestra todas las ventanas de la aplicación en todos los escritorios, o deja de hacerlo.
    /// Todavía no lo atiende ningún manejador, así que responde Unsupported.
    /// </summary>
    TogglePinApplication,

    /// <summary>Devuelve las ventanas a donde estaban con esta configuración de pantallas.</summary>
    RestoreWindowLayout,

    /// <summary>Coloca la ventana activa en una zona del monitor donde está.</summary>
    MoveWindowToZone,

    /// <summary>Opens the overview of every desktop (spec section 17).</summary>
    ShowAllDesktops,

    /// <summary>Closes it and gives the foreground back to where it was.</summary>
    HideAllDesktops,

    /// <summary>
    /// Opens the overview this application draws itself: every desktop side by side, with live
    /// thumbnails and drag and drop between them.
    /// </summary>
    ShowDesktopGrid,

    /// <summary>Cierra la vista propia de escritorios.</summary>
    HideDesktopGrid,

    /// <summary>Opens it when it is closed and closes it when it is open.</summary>
    ToggleDesktopGrid,

    /// <summary>
    /// Rolls the shell down from the top of the screen, or back up if it is already down.
    /// </summary>
    ToggleDropDownConsole,

    /// <summary>
    /// Fija la consola desplegable para que no se recoja al pasar a otra ventana, o la suelta.
    /// </summary>
    ToggleDropDownConsolePin
}
