namespace VirtualDeskManager.Windows.Native;

/// <summary>Win32 constants. Declared once for the whole solution (spec section 39).</summary>
public static class NativeConstants
{
    // Window messages
    /// <summary>Pregunta a una ventana que hay en un punto (barra de titulo, borde, boton...); la respuesta es un <c>HT*</c>.</summary>
    public const uint WM_NCHITTEST = 0x0084;
    /// <summary>La ventana se esta destruyendo; ya no se ve.</summary>
    public const uint WM_DESTROY = 0x0002;
    /// <summary>Pide a una ventana que se cierre; la aplicacion puede preguntar antes o negarse.</summary>
    public const uint WM_CLOSE = 0x0010;
    /// <summary>Hace que <c>GetMessage</c> devuelva 0 y el bucle de mensajes termine. No va a ninguna ventana.</summary>
    public const uint WM_QUIT = 0x0012;
    /// <summary>Se ha pulsado un atajo registrado con <c>RegisterHotKey</c>; su identificador va en <c>wParam</c>.</summary>
    public const uint WM_HOTKEY = 0x0312;

    /// <summary>Ha cambiado la resolucion o el numero de pantallas.</summary>
    public const uint WM_DISPLAYCHANGE = 0x007E;

    /// <summary>El equipo suspende o vuelve del reposo.</summary>
    public const uint WM_POWERBROADCAST = 0x0218;

    /// <summary>Ha vuelto del reposo por accion del usuario.</summary>
    public const int PBT_APMRESUMESUSPEND = 0x0007;

    /// <summary>Ha vuelto del reposo solo, por ejemplo por una tarea programada.</summary>
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    /// <summary>Primer numero de mensaje libre para uso propio de la aplicacion.</summary>
    public const uint WM_APP = 0x8000;

    /// <summary>Ha cambiado el contenido del portapapeles; llega a las ventanas apuntadas con <c>AddClipboardFormatListener</c>.</summary>
    public const uint WM_CLIPBOARDUPDATE = 0x031D;

    // Window styles
    /// <summary>Indice de <c>GetWindowLongPtr</c>/<c>SetWindowLongPtr</c> para los estilos <c>WS_*</c>.</summary>
    public const int GWL_STYLE = -16;
    /// <summary>Indice de <c>GetWindowLongPtr</c>/<c>SetWindowLongPtr</c> para los estilos extendidos <c>WS_EX_*</c>.</summary>
    public const int GWL_EXSTYLE = -20;

    /// <summary>Siempre encima de las ventanas que no lo son.</summary>
    public const long WS_EX_TOPMOST = 0x00000008L;
    /// <summary>Ventana de herramientas: no sale en la barra de tareas ni en Alt+Tab.</summary>
    public const long WS_EX_TOOLWINDOW = 0x00000080L;

    /// <summary>La ventana no recibe el raton: los clics pasan a traves de ella.</summary>
    public const long WS_EX_TRANSPARENT = 0x00000020L;
    /// <summary>La ventana no se activa al hacer clic en ella ni le quita el foco a la que lo tiene.</summary>
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    /// <summary>Needed before <c>SetLayeredWindowAttributes</c> can make a window translucent.</summary>
    public const long WS_EX_LAYERED = 0x00080000L;

    // The frame of an ordinary window: caption, resize border and the three caption buttons.
    // Taken off together to turn a terminal into a bare panel.
    /// <summary>Barra de titulo (incluye <see cref="WS_BORDER"/>).</summary>
    public const long WS_CAPTION = 0x00C00000L;
    /// <summary>Borde que permite redimensionar.</summary>
    public const long WS_THICKFRAME = 0x00040000L;
    /// <summary>Menu de sistema y boton de cerrar en la barra de titulo.</summary>
    public const long WS_SYSMENU = 0x00080000L;
    /// <summary>Boton de minimizar.</summary>
    public const long WS_MINIMIZEBOX = 0x00020000L;
    /// <summary>Boton de maximizar.</summary>
    public const long WS_MAXIMIZEBOX = 0x00010000L;
    /// <summary>Borde fino de una linea.</summary>
    public const long WS_BORDER = 0x00800000L;

    // SetLayeredWindowAttributes
    /// <summary>Usar el color clave: los pixeles de ese color salen transparentes.</summary>
    public const int LWA_COLORKEY = 0x00000001;
    /// <summary>Usar el valor alfa para toda la ventana.</summary>
    public const int LWA_ALPHA = 0x00000002;

    // Hook de teclado de bajo nivel
    /// <summary>Tipo de hook de <c>SetWindowsHookEx</c>: teclado de bajo nivel, de todo el escritorio.</summary>
    public const int WH_KEYBOARD_LL = 13;
    /// <summary>Codigo del callback de un hook que trae un evento que hay que procesar; con otro valor se pasa sin mirar.</summary>
    public const int HC_ACTION = 0;

    /// <summary>Tecla pulsada, sin Alt.</summary>
    public const uint WM_KEYDOWN = 0x0100;
    /// <summary>Tecla soltada, sin Alt.</summary>
    public const uint WM_KEYUP = 0x0101;
    /// <summary>Tecla pulsada con Alt, o F10 sola.</summary>
    public const uint WM_SYSKEYDOWN = 0x0104;
    /// <summary>Tecla soltada con Alt, o F10 sola.</summary>
    public const uint WM_SYSKEYUP = 0x0105;

    /// <summary>LLKHF_INJECTED: el evento lo genero un programa, no un dedo.</summary>
    public const uint LLKHF_INJECTED = 0x00000010;

    /// <summary>Control izquierdo.</summary>
    public const ushort VK_LCONTROL = 0xA2;
    /// <summary>Control derecho.</summary>
    public const ushort VK_RCONTROL = 0xA3;
    /// <summary>Alt izquierdo.</summary>
    public const ushort VK_LMENU = 0xA4;
    /// <summary>Alt derecho (AltGr en teclados espanoles).</summary>
    public const ushort VK_RMENU = 0xA5;
    /// <summary>Mayusculas izquierda.</summary>
    public const ushort VK_LSHIFT = 0xA0;
    /// <summary>Mayusculas derecha.</summary>
    public const ushort VK_RSHIFT = 0xA1;

    // SetWindowPos
    /// <summary>Para <c>SetWindowPos</c>: arriba del todo del orden Z (sin hacerla siempre encima).</summary>
    public static readonly nint HWND_TOP = 0;
    /// <summary>Para <c>SetWindowPos</c>: abajo del todo del orden Z.</summary>
    public static readonly nint HWND_BOTTOM = 1;
    /// <summary>Para <c>SetWindowPos</c>: siempre encima.</summary>
    public static readonly nint HWND_TOPMOST = -1;
    /// <summary>Para <c>SetWindowPos</c>: deja de estar siempre encima.</summary>
    public static readonly nint HWND_NOTOPMOST = -2;

    /// <summary><c>SetWindowPos</c> conserva el tamano (ignora ancho y alto).</summary>
    public const uint SWP_NOSIZE = 0x0001;
    /// <summary><c>SetWindowPos</c> conserva la posicion (ignora x e y).</summary>
    public const uint SWP_NOMOVE = 0x0002;
    /// <summary><c>SetWindowPos</c> conserva el orden Z (ignora la ventana de insercion).</summary>
    public const uint SWP_NOZORDER = 0x0004;
    /// <summary><c>SetWindowPos</c> no activa la ventana.</summary>
    public const uint SWP_NOACTIVATE = 0x0010;
    /// <summary><c>SetWindowPos</c> ademas la muestra.</summary>
    public const uint SWP_SHOWWINDOW = 0x0040;

    // ShowWindow
    /// <summary>Oculta la ventana y activa otra.</summary>
    public const int SW_HIDE = 0;
    /// <summary>Muestra y activa la ventana en su tamano normal, restaurandola si estaba minimizada o maximizada.</summary>
    public const int SW_SHOWNORMAL = 1;
    /// <summary>Muestra la ventana minimizada y la activa.</summary>
    public const int SW_SHOWMINIMIZED = 2;
    /// <summary>Maximiza la ventana.</summary>
    public const int SW_MAXIMIZE = 3;
    /// <summary>Muestra y activa la ventana con su tamano y posicion actuales.</summary>
    public const int SW_SHOW = 5;
    /// <summary>Minimiza la ventana y activa la siguiente.</summary>
    public const int SW_MINIMIZE = 6;
    /// <summary>Restaura la ventana minimizada o maximizada a su tamano y posicion normales, y la activa.</summary>
    public const int SW_RESTORE = 9;

    // GetWindow
    /// <summary>Para <c>GetWindow</c>: la ventana propietaria.</summary>
    public const uint GW_OWNER = 4;

    // Message only window
    /// <summary>Padre que hace de una ventana una ventana solo de mensajes: invisible y fuera de la enumeracion.</summary>
    public static readonly nint HWND_MESSAGE = -3;

    // WinEvents
    /// <summary>Ha cambiado la ventana en primer plano.</summary>
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    /// <summary>Se ha creado un objeto (ventana u otra cosa: mirar <c>idObject</c>).</summary>
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    /// <summary>Se ha destruido un objeto.</summary>
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    /// <summary>Un objeto se ha hecho visible.</summary>
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    /// <summary>Un objeto se ha ocultado.</summary>
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    /// <summary>Ha cambiado el nombre de un objeto; en una ventana, su titulo.</summary>
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    /// <summary>Un objeto ha cambiado de posicion o tamano. Llega muy a menudo, tambien por el cursor de texto.</summary>
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    /// <summary>Ha cambiado el orden Z de los hijos de un objeto.</summary>
    public const uint EVENT_OBJECT_REORDER = 0x8004;
    /// <summary>El usuario empieza a mover o redimensionar una ventana.</summary>
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    /// <summary>El usuario termina de mover o redimensionar una ventana.</summary>
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    /// <summary>Una ventana se va a minimizar.</summary>
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    /// <summary>Una ventana deja de estar minimizada.</summary>
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

    /// <summary>STATE_SYSTEM_INVISIBLE, used with TITLEBARINFO.rgstate.</summary>
    public const uint STATE_SYSTEM_INVISIBLE = 0x00008000;

    // Answers to WM_NCHITTEST. HTCAPTION is the only one that means "free caption": the user can
    // drag the window there, so nothing of the application is in the way.
    /// <summary>Fuera de la ventana o sobre el fondo de la pantalla.</summary>
    public const int HTNOWHERE = 0;
    /// <summary>En el area cliente.</summary>
    public const int HTCLIENT = 1;
    /// <summary>En la barra de titulo, en una zona libre desde la que se puede arrastrar.</summary>
    public const int HTCAPTION = 2;
    /// <summary>En el icono del menu de sistema.</summary>
    public const int HTSYSMENU = 3;
    /// <summary>En el boton de minimizar.</summary>
    public const int HTMINBUTTON = 8;
    /// <summary>En el boton de maximizar.</summary>
    public const int HTMAXBUTTON = 9;
    /// <summary>En el boton de cerrar.</summary>
    public const int HTCLOSE = 20;

    // SendMessageTimeout
    /// <summary>No esperar si la aplicacion destino parece colgada; volver enseguida.</summary>
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>La ventana no tiene superficie de redireccion: lo que se ve lo pone DirectComposition o el DWM.</summary>
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    /// <summary>
    /// El callback de <c>SetWinEventHook</c> corre en este proceso y llega por la cola de mensajes del
    /// hilo que lo instalo; no inyecta nada en otros procesos.
    /// </summary>
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    /// <summary>No avisar de los eventos que provoquen las ventanas de este mismo proceso.</summary>
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    /// <summary><c>idObject</c> de un WinEvent cuando el evento es de la ventana en si.</summary>
    public const int OBJID_WINDOW = 0;
    /// <summary><c>idChild</c> de un WinEvent cuando el evento es del objeto, no de un hijo suyo.</summary>
    public const int CHILDID_SELF = 0;

    // Hotkeys
    /// <summary>Modificador de atajo: Alt.</summary>
    public const uint MOD_ALT = 0x0001;
    /// <summary>Modificador de atajo: Control.</summary>
    public const uint MOD_CONTROL = 0x0002;
    /// <summary>Modificador de atajo: Mayusculas.</summary>
    public const uint MOD_SHIFT = 0x0004;
    /// <summary>Modificador de atajo: tecla Windows.</summary>
    public const uint MOD_WIN = 0x0008;
    /// <summary>Que mantener pulsado el atajo no lo repita.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    // Virtual keys used by the navigation service
    /// <summary>Mayusculas (cualquiera de las dos).</summary>
    public const ushort VK_SHIFT = 0x10;
    /// <summary>Control (cualquiera de los dos).</summary>
    public const ushort VK_CONTROL = 0x11;
    /// <summary>Alt (cualquiera de los dos).</summary>
    public const ushort VK_MENU = 0x12;
    /// <summary>Flecha izquierda.</summary>
    public const ushort VK_LEFT = 0x25;
    /// <summary>Flecha derecha.</summary>
    public const ushort VK_RIGHT = 0x27;
    /// <summary>Tecla Windows izquierda.</summary>
    public const ushort VK_LWIN = 0x5B;
    /// <summary>Tecla Windows derecha.</summary>
    public const ushort VK_RWIN = 0x5C;
    /// <summary>Tecla D.</summary>
    public const ushort VK_D = 0x44;
    /// <summary>F4: con Ctrl+Win, cierra el escritorio que se está viendo.</summary>
    public const ushort VK_F4 = 0x73;
    /// <summary>Tabulador.</summary>
    public const ushort VK_TAB = 0x09;
    /// <summary>Escape.</summary>
    public const ushort VK_ESCAPE = 0x1B;

    // SendInput
    /// <summary>Tipo de <c>INPUT</c>: evento de teclado.</summary>
    public const uint INPUT_KEYBOARD = 1;
    /// <summary>La tecla es extendida (flechas, Windows...): sin esto, algunas se confunden con las del teclado numerico.</summary>
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    /// <summary>El evento es soltar la tecla; sin esta bandera, pulsarla.</summary>
    public const uint KEYEVENTF_KEYUP = 0x0002;

    // Process access
    /// <summary>Permiso minimo para consultar un proceso (ruta, identificador de aplicacion); se concede casi siempre sin ser administrador.</summary>
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // Win32 error codes worth mapping to an OperationStatus
    /// <summary>Error 5: acceso denegado.</summary>
    public const int ERROR_ACCESS_DENIED = 5;
    /// <summary>Error 1400: el asa de ventana no es valida (la ventana ya no existe).</summary>
    public const int ERROR_INVALID_WINDOW_HANDLE = 1400;
    /// <summary>Error 1409: el atajo ya lo tiene registrado otro programa o el shell.</summary>
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    // HRESULTs
    /// <summary>HRESULT de exito.</summary>
    public const int S_OK = 0;
    /// <summary>HRESULT de acceso denegado.</summary>
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);
    /// <summary>HRESULT de argumento no valido.</summary>
    public const int E_INVALIDARG = unchecked((int)0x80070057);
}
