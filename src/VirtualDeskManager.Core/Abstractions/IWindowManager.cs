using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>
/// Everything the application needs from Win32 windows (spec section 8).
/// </summary>
/// <remarks>
/// The spec sketches the mutating members returning <c>bool</c>; they return
/// <see cref="OperationResult"/> instead so that "access denied on an elevated window" is not
/// silently indistinguishable from "it did not work" (spec sections 19, 35 and 36).
/// </remarks>
public interface IWindowManager
{
    /// <summary>La ventana que está en primer plano, o 0 si no hay ninguna.</summary>
    nint GetForegroundWindow();

    /// <summary>Foto de los datos de una ventana, o null si ya no existe o no se pudo leer.</summary>
    WindowInfo? GetWindowInfo(nint hwnd);

    /// <summary>Las ventanas de primer nivel que pasan <see cref="IsManageableWindow"/>, en el orden en que las da Windows.</summary>
    IReadOnlyList<WindowInfo> GetTopLevelWindows();

    /// <summary>Si el identificador sigue siendo el de una ventana viva. 0 nunca lo es.</summary>
    bool IsWindow(nint hwnd);

    /// <summary>Si la ventana está marcada como «siempre encima».</summary>
    bool IsAlwaysOnTop(nint hwnd);

    /// <summary>True for real, user facing top level windows: the ones rules should look at.</summary>
    bool IsManageableWindow(nint hwnd);

    /// <summary>El proceso dueño de la ventana, o 0 si la ventana ya no existe.</summary>
    uint GetWindowProcessId(nint hwnd);

    /// <summary>Pone o quita la ventana de «siempre encima» sin activarla.</summary>
    OperationResult SetAlwaysOnTop(nint hwnd, bool enabled);

    /// <summary>
    /// Mueve y redimensiona la ventana, en píxeles de pantalla, sin activarla ni cambiar su orden.
    /// El rectángulo incluye la franja transparente que Windows deja alrededor; para que cuadre el
    /// borde visible está <see cref="MoveWindowVisible"/>.
    /// </summary>
    OperationResult MoveWindow(nint hwnd, int x, int y, int width, int height);

    /// <summary>
    /// Como <see cref="MoveWindow"/>, pero hace que sea el borde <b>visible</b> el que caiga en el
    /// rectangulo pedido, descontando la franja transparente que Windows deja alrededor.
    /// </summary>
    OperationResult MoveWindowVisible(nint hwnd, int x, int y, int width, int height);

    /// <summary>Minimiza, maximiza o restaura la ventana.</summary>
    OperationResult SetWindowState(nint hwnd, WindowVisualState state);

    /// <summary>Trae la ventana al frente y le da el foco, restaurándola antes si estaba minimizada.</summary>
    OperationResult ActivateWindow(nint hwnd);

    /// <summary>
    /// Asks a window to close, the way its own close button does.
    /// </summary>
    /// <remarks>
    /// Asks rather than forces, and that distinction is the whole safety of it: the application
    /// gets to put up "save your changes?" and to refuse. Killing the window outright would take
    /// whatever was in it with no way back, which is not a decision this program should be making
    /// on anyone's behalf.
    ///
    /// Success therefore means the request was delivered, not that the window has gone.
    /// </remarks>
    OperationResult CloseWindow(nint hwnd);

    /// <summary>
    /// Los monitores del equipo, en el orden en que los da Windows.
    /// </summary>
    /// <remarks>
    /// Única fuente del número de monitor: lo usan tanto el desplegable del editor como el
    /// movimiento, para que «Monitor 2» signifique la misma pantalla en los dos sitios.
    /// </remarks>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>
    /// El monitor donde está el puntero del ratón, o null si no se puede averiguar.
    /// </summary>
    /// <remarks>
    /// Es el criterio para decidir "dónde estoy mirando" cuando hay varias pantallas. Podría
    /// usarse la ventana activa, pero el ratón es más fiel a la intención: quien pulsa un atajo
    /// suele tener el cursor en la pantalla a la que está atendiendo, aunque el foco de teclado
    /// se haya quedado en otra.
    /// </remarks>
    MonitorInfo? GetMonitorAtCursor();

    /// <summary>
    /// Lleva una ventana al monitor indicado, contando desde uno.
    /// </summary>
    /// <remarks>
    /// Conserva el tamaño de la ventana y, si estaba maximizada, la deja maximizada en el monitor
    /// nuevo: mover una ventana de pantalla no debería ser también cambiarle la forma.
    /// </remarks>
    OperationResult MoveWindowToMonitor(nint hwnd, int monitorNumber);

    /// <summary>
    /// Turns a window of this application into an overlay: a tool window that never takes the
    /// focus, so the window underneath keeps looking and behaving as the active one.
    /// </summary>
    /// <remarks>
    /// It can be turned off for a moment: a menu needs the focus to be able to close itself when
    /// the user clicks somewhere else.
    /// </remarks>
    OperationResult SetNonActivating(nint hwnd, bool enabled);

    /// <summary>Hace que el raton atraviese la ventana, para superposiciones que solo dibujan.</summary>
    OperationResult SetClickThrough(nint hwnd, bool enabled);

    /// <summary>Como tiene las esquinas la ventana, o null si Windows no lo sabe decir.</summary>
    int? GetCornerPreference(nint hwnd);

    /// <summary>Le pone a la ventana un redondeo de esquinas concreto.</summary>
    OperationResult SetCornerPreference(nint hwnd, int preference);

    /// <summary>Atajo para cuadrarlas o devolverlas al valor por defecto de Windows.</summary>
    OperationResult SetSquareCorners(nint hwnd, bool enabled);

    /// <summary>Quita o repone el filo de un pixel que Windows 11 pinta alrededor de la ventana.</summary>
    OperationResult SetBorderVisible(nint hwnd, bool visible);

    /// <summary>
    /// Moves a window and puts it back at the very top, without activating it.
    /// </summary>
    /// <remarks>
    /// Needed by the floating button: when the window underneath is set to always on top it joins
    /// the same band and ends up over the button, which then has to climb back above it.
    /// </remarks>
    OperationResult PlaceOnTop(nint hwnd, int x, int y, int width, int height);
}
