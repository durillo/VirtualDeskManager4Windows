using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>How left / right desktop navigation is performed (spec section 15).</summary>
public enum DesktopNavigationMode
{
    /// <summary>Synthesizes the shell shortcut. Works on the public backend, no internal COM.</summary>
    SendInput,

    /// <summary>Navigation reports Unsupported.</summary>
    Disabled
}

/// <summary>El diario de la aplicación.</summary>
public sealed class LoggingSettings
{
    /// <summary>Escribir el diario. Apagado equivale a nivel <see cref="LogLevel.None"/>. Encendido de serie.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nivel mínimo que se escribe cuando el diario está encendido. De serie, <see cref="LogLevel.Information"/>.</summary>
    public LogLevel Level { get; set; } = LogLevel.Information;
}


/// <summary>Copias de seguridad de la configuración.</summary>
public sealed class BackupSettings
{
    /// <summary>Guardar el estado anterior cada vez que algo cambia.</summary>
    public bool AutomaticHistory { get; set; } = true;

    /// <summary>Cuántas copias del historial se conservan antes de ir borrando las viejas.</summary>
    /// <remarks>
    /// Veinte copias de 8 KB son 160 KB. El límite no está por el espacio sino para que la carpeta
    /// siga siendo legible cuando alguien la abra buscando la de antes de ayer.
    /// </remarks>
    public int HistoryLimit { get; set; } = 20;

    /// <summary>Raíz de la carpeta sincronizada elegida. Vacío cuando no hay ninguna.</summary>
    public string CloudFolder { get; set; } = "";

    /// <summary>Dejar una copia en esa carpeta cada vez que la configuración cambie.</summary>
    public bool CopyToCloud { get; set; }
}
/// <summary>Contents of settings.json (spec section 25).</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Versión del formato del fichero. Si es mayor que la que entiende esta compilación, se avisa
    /// y los valores desconocidos se ignoran.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Arrancar con Windows. Es lo que el usuario eligió; el estado real lo lleva
    /// <see cref="Abstractions.IStartupService"/>.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Arrancar sin abrir la ventana principal, sólo con el icono de la bandeja. Activado de serie.</summary>
    /// <remarks>Sin icono en la bandeja se ignora: no habría forma de volver a la aplicación.</remarks>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Mostrar el icono de la bandeja del sistema. Se lee al arrancar.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>La ventana principal, en oscuro. Se cambia con la luna de su barra.</summary>
    public bool VdeskDark { get; set; }

    /// <summary>Follow the window after moving it to another desktop (spec section 16).</summary>
    public bool FollowMovedWindow { get; set; } = true;

    /// <summary>
    /// When a rule opens an application on another desktop, ask whether to go there.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FollowMovedWindow"/> on purpose: that one is about a move the user
    /// asked for, and this one about a move a rule decided, where jumping without asking would
    /// yank the screen away in the middle of something else.
    /// </remarks>
    public bool AskToFollowRuleMoves { get; set; } = true;

    /// <summary>Apply the rules to the windows that are already open when the app starts.</summary>
    public bool ApplyRulesOnStartup { get; set; } = true;

    /// <summary>Cómo se pasa al escritorio de la izquierda o de la derecha. De serie, <see cref="DesktopNavigationMode.SendInput"/>.</summary>
    public DesktopNavigationMode DesktopNavigation { get; set; } = DesktopNavigationMode.SendInput;

    /// <summary>
    /// Recreate the missing desktops at start up until there are as many as the last time, and
    /// put their names back.
    /// </summary>
    /// <remarks>
    /// Only ever adds. If there are more desktops than were saved, nothing is removed: taking a
    /// desktop away could take the windows on it out of sight, and no saved setting justifies
    /// that. Deleting one on purpose while the application is running is recorded straight away,
    /// so it does not come back.
    /// </remarks>
    public bool RestoreDesktopLayout { get; set; } = true;

    /// <summary>The desktops of the last session, by position (spec section 15).</summary>
    public DesktopLayout Desktops { get; set; } = new();

    /// <summary>
    /// Whether the Ctrl+Alt+1..9 shortcuts have already been offered to this installation.
    /// </summary>
    /// <remarks>
    /// They arrived after the first version, so anyone who already had a hotkeys.json would never
    /// have seen them: the defaults are only written when the file does not exist. They are added
    /// once, and this flag is what stops them coming back for someone who deleted them on purpose.
    /// </remarks>
    public bool NumberedDesktopHotkeysAdded { get; set; }

    /// <summary>The same, for the show / hide all desktops shortcuts.</summary>
    public bool TaskViewHotkeysAdded { get; set; }

    /// <summary>The same, for the shortcut of the overview this application draws itself.</summary>
    public bool DesktopGridHotkeyAdded { get; set; }

    /// <summary>The same, for the shortcut of the drop down console.</summary>
    public bool DropDownConsoleHotkeyAdded { get; set; }

    /// <summary>Lo mismo, para el atajo que fija la consola (1.0.0-beta.3).</summary>
    public bool DropDownConsolePinHotkeyAdded { get; set; }

    /// <summary>The shell that slides down from the top of the screen.</summary>
    public DropDownConsoleSettings DropDownConsole { get; set; } = new();

    /// <summary>Show the current desktop on the tray icon, number inside and name in the tooltip.</summary>
    public bool ShowDesktopInTray { get; set; } = true;

    /// <summary>Floating button on the title bar of the active window.</summary>
    public bool ShowTitleBarButton { get; set; } = true;

    /// <summary>
    /// Extra pixels between the button and the right edge of the caption. Applications that draw
    /// their own title bar (browsers, Electron) may need a nudge.
    /// </summary>
    public int TitleBarButtonOffset { get; set; }

    /// <summary>Tamaño, color y transparencia de ese botón.</summary>
    public TitleBarButtonSettings TitleBarButton { get; set; } = new();

    /// <summary>Los distintivos de escritorio sobre los botones de la barra de tareas.</summary>
    public TaskbarBadgeSettings TaskbarBadges { get; set; } = new();

    /// <summary>La actualización por internet.</summary>
    public UpdateSettings Updates { get; set; } = new();

    /// <summary>
    /// Interface language: an ISO 639-1 code such as <c>es</c>, or <c>auto</c> to follow Windows.
    /// </summary>
    public string Language { get; set; } = LocalizationService.AutomaticCode;

    /// <summary>Copias de seguridad de la configuración.</summary>
    public BackupSettings Backup { get; set; } = new();

    /// <summary>
    /// Atrapar con un gancho de teclado las combinaciones que Windows se reserva.
    /// </summary>
    /// <remarks>
    /// Sin esto, Ctrl+Esc abre el menú Inicio y no hay forma de pedírselo a Windows: RegisterHotKey
    /// se niega a dar las suyas. Un gancho de bajo nivel las ve antes que el shell.
    ///
    /// Sólo se usa para lo que Windows haya rechazado, nunca para lo que da por las buenas.
    /// </remarks>
    public bool InterceptReservedHotkeys { get; set; } = true;

    /// <summary>Recordar dónde estaba cada ventana y devolverla a su sitio.</summary>
    public WindowLayoutSettings WindowLayout { get; set; } = new();

    /// <summary>Zonas por monitor.</summary>
    public ZoneSettings Zones { get; set; } = new();

    /// <summary>El diario: si se escribe y desde qué nivel.</summary>
    public LoggingSettings Logging { get; set; } = new();
}
