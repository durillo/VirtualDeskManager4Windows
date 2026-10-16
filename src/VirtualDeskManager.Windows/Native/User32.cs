using System.Runtime.InteropServices;
using System.Text;

namespace VirtualDeskManager.Windows.Native;

/// <summary>
/// user32.dll entry points. Declared once, never duplicated anywhere else (spec section 39).
/// </summary>
public static class User32
{
    /// <summary>Procedimiento de ventana: recibe cada mensaje que llega a una ventana propia.</summary>
    /// <remarks>
    /// El delegado que se pase a Windows hay que guardarlo en un campo mientras viva la ventana: si
    /// el recolector se lo lleva, el siguiente mensaje salta a memoria liberada.
    /// </remarks>
    public delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Callback de <see cref="SetWinEventHook"/>: un evento de accesibilidad (foco, movimiento, creación...).</summary>
    /// <remarks>
    /// Con <c>idObject</c> distinto de <c>OBJID_WINDOW</c> (0) el evento es de una parte de la
    /// ventana (cursor, barra de desplazamiento...), no de la ventana. Como con
    /// <see cref="WndProcDelegate"/>, el delegado tiene que seguir vivo mientras dure el hook.
    /// </remarks>
    public delegate void WinEventProcDelegate(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    /// <summary>Callback de <see cref="EnumWindows"/>: se llama por cada ventana de nivel superior; false corta la enumeración.</summary>
    public delegate bool EnumWindowsProcDelegate(nint hWnd, nint lParam);

    // ---------------------------------------------------------------- entrada

    /// <summary>Para <see cref="GetLastInputInfo"/>: <c>cbSize</c> hay que ponerlo antes de llamar.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        /// <summary>El tamaño de la estructura.</summary>
        public uint cbSize;

        /// <summary>El <c>GetTickCount</c> de la última entrada.</summary>
        public uint dwTime;
    }

    /// <summary>Cuándo fue la última tecla o movimiento del ratón de esta sesión, en ticks de <c>GetTickCount</c>.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    // ---------------------------------------------------------------- windows

    /// <summary>La ventana en primer plano, la que recibe el teclado; 0 si no hay ninguna (por ejemplo, mientras cambia el foco).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetForegroundWindow();

    /// <summary>Pone una ventana en primer plano y le da el foco del teclado.</summary>
    /// <remarks>
    /// Windows lo limita: si el proceso que llama no tiene el primer plano ni acaba de recibir
    /// entrada del usuario, puede negarse y limitarse a hacer parpadear el botón de la barra de
    /// tareas. Por eso el resultado hay que comprobarlo.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    /// <summary>Firma del callback de un hook de teclado de bajo nivel.</summary>
    public delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    /// <summary>
    /// Instala un hook de teclado de bajo nivel.
    /// </summary>
    /// <remarks>
    /// Es la única forma de ver las combinaciones con la tecla Windows: el shell se queda con
    /// casi todas antes de que lleguen a ninguna ventana, de modo que un manejador de teclado
    /// normal no las ve nunca.
    ///
    /// El hilo que lo instala necesita bomba de mensajes, y el callback tiene que volver en unos
    /// pocos milisegundos o Windows lo desengancha sin avisar.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, nint module, uint threadId);

    /// <summary>Quita un hook instalado con <see cref="SetWindowsHookEx"/>.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    /// <summary>Pasa el evento al siguiente hook de la cadena.</summary>
    /// <remarks>
    /// Un callback de hook que no se trague la tecla tiene que llamarla y devolver lo que ella
    /// devuelva; devolver un valor distinto de cero sin llamarla se come el evento.
    /// </remarks>
    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    /// <summary>MONITORINFOF_PRIMARY.</summary>
    public const uint MONITORINFOF_PRIMARY = 1;

    /// <summary>Dónde está el puntero ahora mismo, en píxeles de la pantalla virtual.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    /// <summary>Callback de <see cref="EnumDisplayMonitors"/>: se llama por cada monitor; false corta la enumeración.</summary>
    public delegate bool MonitorEnumProc(nint monitor, nint hdc, ref RECT clip, nint data);

    /// <summary>Recorre los monitores conectados; con <paramref name="hdc"/> y <paramref name="clip"/> a 0, todos los del escritorio.</summary>
    /// <param name="hdc">Contexto de dispositivo al que limitarse; 0 para todos.</param>
    /// <param name="clip">Rectángulo al que limitarse; 0 para ninguno.</param>
    /// <param name="callback">Se llama una vez por monitor.</param>
    /// <param name="data">Valor que se pasa tal cual al callback.</param>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    /// <summary>Rectángulo de un monitor y su área de trabajo (sin la barra de tareas), en píxeles de la pantalla virtual.</summary>
    /// <remarks>Antes de llamar hay que poner en <c>cbSize</c> el tamaño de la estructura.</remarks>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    /// <summary>MAPVK_VK_TO_CHAR: the character a virtual key produces on the current layout.</summary>
    public const uint MAPVK_VK_TO_CHAR = 2;

    /// <summary>Traduce entre tecla virtual, código de escaneo y carácter según la distribución de teclado actual.</summary>
    /// <remarks>
    /// Con <paramref name="mapType"/> 0 (<c>MAPVK_VK_TO_VSC</c>) da el código de escaneo de una tecla
    /// virtual. Con <see cref="MAPVK_VK_TO_CHAR"/> da el carácter, y el bit alto marca tecla
    /// muerta: por eso se enmascara con <c>0x7FFF</c>. Devuelve 0 si no hay traducción.
    /// </remarks>
    /// <param name="code">Tecla virtual o código de escaneo, según <paramref name="mapType"/>.</param>
    /// <param name="mapType">Tipo de traducción (<c>MAPVK_*</c>).</param>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>
    /// Makes a window uniformly translucent. The window needs <c>WS_EX_LAYERED</c> first.
    /// </summary>
    /// <remarks>
    /// This and not the WPF <c>Opacity</c> property: the window being faded belongs to another
    /// process, so there is nothing of ours to set a property on. It applies to the whole window,
    /// child controls included, which is what makes a whole terminal fade as one.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint colorKey, byte alpha, int flags);

    /// <summary>Si el asa corresponde a una ventana que todavía existe.</summary>
    /// <remarks>Las asas se reutilizan: true no garantiza que sea la misma ventana de antes.</remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hWnd);

    /// <summary>Si la ventana tiene el estilo <c>WS_VISIBLE</c>.</summary>
    /// <remarks>
    /// No dice que se vea: una ventana tapada, fuera de pantalla o en otro escritorio virtual
    /// (ocultada por el shell con <c>DWMWA_CLOAKED</c>) sigue dando true.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    /// <summary>Si la ventana está minimizada.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint hWnd);

    /// <summary>Si la ventana está maximizada.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(nint hWnd);

    /// <summary>Copia el título de la ventana; devuelve los caracteres copiados, 0 si no tiene o hay error.</summary>
    /// <remarks>Mejor <see cref="GetWindowTitle"/>, que dimensiona el búfer.</remarks>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    /// <summary>Longitud en caracteres del título de la ventana, sin el nulo final.</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    public static extern int GetWindowTextLength(nint hWnd);

    /// <summary>Copia el nombre de la clase de la ventana; devuelve los caracteres copiados, 0 si falla.</summary>
    /// <remarks>Mejor <see cref="GetWindowClassName"/>. Un nombre de clase no pasa de 256 caracteres.</remarks>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    public static extern int GetClassName(nint hWnd, StringBuilder text, int maxCount);

    /// <summary>Devuelve el hilo que creó la ventana y deja en <paramref name="processId"/> su proceso; 0 si la ventana no existe.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    /// <summary>Lee un valor de la ventana: estilos (<c>GWL_STYLE</c>, <c>GWL_EXSTYLE</c>), propietario, etc.</summary>
    /// <remarks>0 puede ser un valor válido; para distinguirlo de un error hay que mirar GetLastError.</remarks>
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hWnd, int index);

    /// <summary>Cambia un valor de la ventana (estilos, procedimiento...) y devuelve el anterior.</summary>
    /// <remarks>
    /// Al cambiar estilos de marco, Windows no repinta solo: hace falta un <see cref="SetWindowPos"/>
    /// con <c>SWP_FRAMECHANGED</c>. Como en <see cref="GetWindowLongPtr"/>, 0 no siempre es error.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

    /// <summary>Rectángulo de la ventana en coordenadas de pantalla, incluida la franja transparente de agarre.</summary>
    /// <remarks>
    /// En Windows 10 y 11 es más grande que lo que se ve; el marco visible lo da
    /// <see cref="Dwmapi.TryGetVisibleBounds"/>.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT rect);

    /// <summary>Mueve, redimensiona o cambia el orden Z de una ventana.</summary>
    /// <remarks>
    /// Coordenadas en píxeles de pantalla y tamaño con la franja transparente incluida, igual que
    /// <see cref="GetWindowRect"/>. Las banderas <c>SWP_*</c> dicen qué se cambia y qué se ignora.
    /// </remarks>
    /// <param name="hWnd">La ventana.</param>
    /// <param name="hWndInsertAfter">Ventana tras la que colocarla en el orden Z, o <c>HWND_TOP</c>, <c>HWND_TOPMOST</c>...</param>
    /// <param name="x">Nueva posición del borde izquierdo.</param>
    /// <param name="y">Nueva posición del borde superior.</param>
    /// <param name="cx">Nuevo ancho.</param>
    /// <param name="cy">Nuevo alto.</param>
    /// <param name="flags">Banderas <c>SWP_*</c>.</param>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    /// <summary>Muestra, oculta, minimiza, maximiza o restaura una ventana según el comando <c>SW_*</c>.</summary>
    /// <remarks>
    /// Devuelve si la ventana era visible antes, no si ha funcionado: para saber si ha ido bien hay
    /// que volver a mirar la ventana después.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, int cmdShow);

    /// <summary>
    /// Una ventana relacionada con la dada según <paramref name="cmd"/> (<c>GW_*</c>): su
    /// propietaria, la siguiente en el orden Z, su primera hija...; 0 si no hay.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint GetWindow(nint hWnd, uint cmd);

    /// <summary>
    /// Works across processes, which is exactly why the floating button uses it instead of trying
    /// to reach into the caption of another application.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTitleBarInfo(nint hWnd, ref TITLEBARINFO info);

    /// <summary>Recorre las ventanas de nivel superior, de arriba abajo en el orden Z.</summary>
    /// <remarks>
    /// Incluye ventanas invisibles y las de todos los escritorios virtuales; quien llama tiene que
    /// filtrar. Devuelve false también cuando el callback corta la enumeración.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProcDelegate callback, nint lParam);

    // ---------------------------------------------------------------- hotkeys

    /// <summary>
    /// Registra un atajo de teclado global: al pulsarlo llega <c>WM_HOTKEY</c> con
    /// <paramref name="id"/> a la ventana <paramref name="hWnd"/>.
    /// </summary>
    /// <remarks>
    /// Falla (<c>ERROR_HOTKEY_ALREADY_REGISTERED</c>) si otro programa o el shell ya tiene la
    /// combinación; casi todas las de la tecla Windows están cogidas.
    /// </remarks>
    /// <param name="hWnd">Ventana que recibirá el mensaje.</param>
    /// <param name="id">Identificador propio del atajo.</param>
    /// <param name="modifiers">Modificadores <c>MOD_*</c>.</param>
    /// <param name="virtualKey">Tecla virtual.</param>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    /// <summary>Quita un atajo registrado con <see cref="RegisterHotKey"/>.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    // ---------------------------------------------------------------- win events

    /// <summary>Se suscribe a un rango de eventos de accesibilidad (<c>EVENT_*</c>) de todas las ventanas.</summary>
    /// <remarks>
    /// Vdesk lo usa con <c>WINEVENT_OUTOFCONTEXT</c>: el callback llega por la cola de mensajes del
    /// hilo que lo instaló, que por tanto necesita bomba de mensajes. Devuelve 0 si falla. El
    /// delegado tiene que seguir vivo hasta <see cref="UnhookWinEvent"/>.
    /// </remarks>
    /// <param name="eventMin">Primer evento del rango.</param>
    /// <param name="eventMax">Último evento del rango.</param>
    /// <param name="moduleHandle">Módulo con el callback; 0 fuera de contexto.</param>
    /// <param name="callback">Lo que se llama con cada evento.</param>
    /// <param name="processId">Proceso a vigilar; 0 para todos.</param>
    /// <param name="threadId">Hilo a vigilar; 0 para todos.</param>
    /// <param name="flags">Banderas <c>WINEVENT_*</c>.</param>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint moduleHandle,
        WinEventProcDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    /// <summary>Quita un hook instalado con <see cref="SetWinEventHook"/>.</summary>
    /// <remarks>Hay que llamarla desde el mismo hilo que lo instaló.</remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hookHandle);

    /// <summary>Apunta la ventana para recibir <c>WM_CLIPBOARDUPDATE</c> cada vez que cambie el portapapeles.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(nint hwnd);

    /// <summary>Deja de avisar a la ventana de los cambios del portapapeles.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(nint hwnd);

    /// <summary>La ventana que puso lo que hay ahora en el portapapeles, o 0 si no hay o no se sabe.</summary>
    /// <remarks>No abre el portapapeles: se puede preguntar sin molestar a nadie.</remarks>
    [DllImport("user32.dll")]
    public static extern nint GetClipboardOwner();

    /// <summary>Un dato del sistema; con <c>SM_REMOTESESSION</c> (0x1000), si la sesión es de Escritorio remoto.</summary>
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    /// <summary>El tiempo máximo entre los dos clics de un doble clic, en milisegundos.</summary>
    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    // ---------------------------------------------------------------- message loop

    /// <summary>Registra una clase de ventana; devuelve su átomo, o 0 si falla.</summary>
    /// <remarks>Falla con <c>ERROR_CLASS_ALREADY_EXISTS</c> si el nombre ya está registrado en este proceso.</remarks>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    /// <summary>Quita una clase de ventana registrada; falla si todavía queda alguna ventana de esa clase.</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterClass(string className, nint instance);

    /// <summary>Crea una ventana de una clase registrada; devuelve su asa, o 0 si falla.</summary>
    /// <remarks>
    /// Con <paramref name="parent"/> igual a <c>HWND_MESSAGE</c> (-3) sale una ventana sólo de
    /// mensajes: invisible, sin orden Z y fuera de <see cref="EnumWindows"/>.
    /// </remarks>
    /// <param name="exStyle">Estilos extendidos <c>WS_EX_*</c>.</param>
    /// <param name="className">Nombre de la clase registrada.</param>
    /// <param name="windowName">Título.</param>
    /// <param name="style">Estilos <c>WS_*</c>.</param>
    /// <param name="x">Posición horizontal.</param>
    /// <param name="y">Posición vertical.</param>
    /// <param name="width">Ancho.</param>
    /// <param name="height">Alto.</param>
    /// <param name="parent">Padre o propietaria; 0 si no tiene.</param>
    /// <param name="menu">Menú o identificador de control hijo.</param>
    /// <param name="instance">Módulo dueño de la clase.</param>
    /// <param name="param">Valor que llega en <c>WM_CREATE</c>.</param>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    public static extern nint CreateWindowEx(
        uint exStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    /// <summary>Destruye una ventana propia.</summary>
    /// <remarks>Sólo funciona desde el hilo que la creó; para ventanas de otros procesos no sirve.</remarks>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hWnd);

    /// <summary>El tratamiento por defecto de Windows para los mensajes que el procedimiento de ventana no maneja.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    public static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Espera al siguiente mensaje de la cola del hilo y lo saca.</summary>
    /// <remarks>
    /// Devuelve distinto de cero con un mensaje normal, 0 con <c>WM_QUIT</c> y -1 si hay error: un
    /// bucle <c>while (GetMessage(...))</c> que no mire el -1 se queda girando para siempre.
    /// </remarks>
    /// <param name="message">El mensaje recibido.</param>
    /// <param name="hWnd">Ventana de la que leer; 0 para todas las del hilo.</param>
    /// <param name="filterMin">Primer mensaje aceptado; 0 y 0 para todos.</param>
    /// <param name="filterMax">Último mensaje aceptado.</param>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    public static extern int GetMessage(out MSG message, nint hWnd, uint filterMin, uint filterMax);

    /// <summary>Convierte las pulsaciones de teclas virtuales en mensajes de carácter (<c>WM_CHAR</c>).</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG message);

    /// <summary>Entrega un mensaje al procedimiento de su ventana.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    public static extern nint DispatchMessage(ref MSG message);

    /// <summary>Deja un mensaje en la cola de la ventana y vuelve sin esperar a que se procese.</summary>
    /// <remarks>
    /// Funciona con ventanas de otros procesos, salvo que tengan más integridad (UIPI): entonces
    /// devuelve false con acceso denegado.
    /// </remarks>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Pone <c>WM_QUIT</c> en la cola del hilo que llama, para que su bucle de mensajes termine.</summary>
    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    /// <summary>
    /// Used to ask a window what there is at a point of its caption. Always with a timeout:
    /// a hung application must never block us.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    public static extern nint SendMessageTimeout(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nint result);

    // ---------------------------------------------------------------- input

    /// <summary>Inyecta pulsaciones de teclado o ratón como si las hiciera el usuario; devuelve cuántas entraron.</summary>
    /// <remarks>
    /// Van a la ventana que tenga el foco, sea la que sea, y se mezclan con lo que el usuario esté
    /// pulsando en ese momento. Si devuelve menos de <paramref name="count"/>, algo las bloqueó
    /// (UIPI, escritorio seguro). <paramref name="size"/> es <c>Marshal.SizeOf&lt;INPUT&gt;()</c>.
    /// </remarks>
    /// <param name="count">Número de elementos de <paramref name="inputs"/>.</param>
    /// <param name="inputs">Los eventos, en orden.</param>
    /// <param name="size">Tamaño en bytes de un <see cref="INPUT"/>.</param>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    /// <summary>Estado de una tecla en este momento, sin pasar por la cola de mensajes.</summary>
    /// <remarks>
    /// El bit alto (<c>0x8000</c>) dice si está pulsada ahora; el bajo, si se pulsó desde la última
    /// consulta, y no es fiable porque lo comparten todos los procesos.
    /// </remarks>
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    /// <summary>Destruye un icono creado por el programa y libera su memoria.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint icon);

    // ---------------------------------------------------------------- helpers

    /// <summary>Título de la ventana, o cadena vacía si no tiene o ya no existe.</summary>
    /// <remarks>
    /// Con ventanas de otros procesos, <c>GetWindowText</c> les manda <c>WM_GETTEXT</c> y espera:
    /// una aplicación colgada puede retener a quien llama.
    /// </remarks>
    public static string GetWindowTitle(nint hWnd)
    {
        int length = GetWindowTextLength(hWnd);

        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(length + 1);
        int copied = GetWindowText(hWnd, builder, builder.Capacity);
        return copied > 0 ? builder.ToString() : string.Empty;
    }

    /// <summary>Nombre de la clase de la ventana, o cadena vacía si ya no existe.</summary>
    public static string GetWindowClassName(nint hWnd)
    {
        StringBuilder builder = new(256);
        int copied = GetClassName(hWnd, builder, builder.Capacity);
        return copied > 0 ? builder.ToString() : string.Empty;
    }
}
