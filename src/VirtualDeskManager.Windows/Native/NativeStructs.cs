using System.Runtime.InteropServices;

namespace VirtualDeskManager.Windows.Native;

/// <summary>RECT de Win32: un rectángulo dado por sus cuatro bordes, en píxeles.</summary>
/// <remarks><see cref="Right"/> y <see cref="Bottom"/> quedan fuera: el ancho es <c>Right - Left</c>, sin sumar uno.</remarks>
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    /// <summary>Borde izquierdo.</summary>
    public int Left;
    /// <summary>Borde superior.</summary>
    public int Top;
    /// <summary>Borde derecho (excluido).</summary>
    public int Right;
    /// <summary>Borde inferior (excluido).</summary>
    public int Bottom;

    /// <summary>Ancho en píxeles.</summary>
    public int Width => Right - Left;

    /// <summary>Alto en píxeles.</summary>
    public int Height => Bottom - Top;
}

/// <summary>KBDLLHOOKSTRUCT: lo que un hook de teclado de bajo nivel recibe de cada tecla.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    /// <summary>Código de tecla virtual.</summary>
    public uint vkCode;

    /// <summary>Código de escaneo del hardware.</summary>
    public uint scanCode;

    /// <summary>Banderas, entre ellas <c>LLKHF_INJECTED</c>.</summary>
    public uint flags;

    /// <summary>Marca de tiempo del evento, en milisegundos del reloj del sistema.</summary>
    public uint time;

    /// <summary>Dato extra que puso quien generó el evento (por ejemplo, con <c>SendInput</c>).</summary>
    public nuint dwExtraInfo;
}

/// <summary>SIZE de Win32: un ancho y un alto.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    /// <summary>Ancho.</summary>
    public int cx;
    /// <summary>Alto.</summary>
    public int cy;
}

/// <summary>
/// DWM_THUMBNAIL_PROPERTIES: where and how a registered thumbnail is drawn.
/// </summary>
/// <remarks>
/// <see cref="dwFlags"/> says which of the other fields were actually filled in, so a partial
/// update never has to read back the ones it does not care about.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct DWM_THUMBNAIL_PROPERTIES
{
    /// <summary>Qué campos traen valor: combinación de las constantes <c>Dwmapi.DWM_TNP_*</c>.</summary>
    public int dwFlags;

    /// <summary>Destination area, in physical pixels, relative to the client area.</summary>
    public RECT rcDestination;

    /// <summary>Parte de la ventana origen que se muestra, relativa a ella.</summary>
    public RECT rcSource;

    /// <summary>Opacidad, de 0 (transparente) a 255 (opaca).</summary>
    public byte opacity;

    /// <summary>Si la miniatura se pinta.</summary>
    [MarshalAs(UnmanagedType.Bool)]
    public bool fVisible;

    /// <summary>True leaves the caption and the borders of the source window out.</summary>
    [MarshalAs(UnmanagedType.Bool)]
    public bool fSourceClientAreaOnly;
}

/// <summary>POINT de Win32: un punto en píxeles; si es de pantalla o de cliente lo dice quien lo da.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    /// <summary>Coordenada horizontal.</summary>
    public int X;
    /// <summary>Coordenada vertical.</summary>
    public int Y;
}

/// <summary>
/// TITLEBARINFO. <c>rgstate</c> has CCHILDREN_TITLEBAR + 1 entries: the title bar itself and its
/// five children (icon, minimize, maximize, help, close).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TITLEBARINFO
{
    /// <summary>Tamaño de la estructura en bytes; hay que ponerlo antes de llamar.</summary>
    public uint cbSize;
    /// <summary>Rectángulo de la barra de título, en coordenadas de pantalla.</summary>
    public RECT rcTitleBar;

    /// <summary>
    /// Estado de la barra y de cada uno de sus elementos (<c>STATE_SYSTEM_*</c>), por ejemplo
    /// <see cref="NativeConstants.STATE_SYSTEM_INVISIBLE"/> para un botón que no está.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public uint[] rgstate;
}

/// <summary>MSG de Win32: un mensaje sacado de la cola de un hilo.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    /// <summary>Ventana destinataria; 0 para mensajes del hilo.</summary>
    public nint hwnd;
    /// <summary>Número del mensaje (<c>WM_*</c>).</summary>
    public uint message;
    /// <summary>Primer parámetro del mensaje.</summary>
    public nint wParam;
    /// <summary>Segundo parámetro del mensaje.</summary>
    public nint lParam;
    /// <summary>Cuándo se puso en la cola, en milisegundos del reloj del sistema.</summary>
    public uint time;
    /// <summary>Posición del cursor en ese momento, en coordenadas de pantalla.</summary>
    public POINT pt;
}

/// <summary>WNDCLASSEX: la descripción de una clase de ventana para <c>RegisterClassEx</c>.</summary>
/// <remarks>
/// El delegado de <see cref="lpfnWndProc"/> tiene que seguir vivo (en un campo) mientras haya
/// ventanas de la clase: el marshalling no lo retiene.
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WNDCLASSEX
{
    /// <summary>Tamaño de la estructura en bytes.</summary>
    public uint cbSize;
    /// <summary>Estilos de clase <c>CS_*</c>.</summary>
    public uint style;
    /// <summary>Procedimiento de ventana de la clase.</summary>
    [MarshalAs(UnmanagedType.FunctionPtr)]
    public User32.WndProcDelegate lpfnWndProc;
    /// <summary>Bytes extra que reservar por clase.</summary>
    public int cbClsExtra;
    /// <summary>Bytes extra que reservar por ventana.</summary>
    public int cbWndExtra;
    /// <summary>Módulo dueño de la clase.</summary>
    public nint hInstance;
    /// <summary>Icono grande; 0 para el de por defecto.</summary>
    public nint hIcon;
    /// <summary>Cursor; 0 para que lo ponga la aplicación.</summary>
    public nint hCursor;
    /// <summary>Pincel de fondo; 0 para que lo pinte la aplicación.</summary>
    public nint hbrBackground;
    /// <summary>Nombre del recurso de menú; null si no hay.</summary>
    public string? lpszMenuName;
    /// <summary>Nombre de la clase.</summary>
    public string lpszClassName;
    /// <summary>Icono pequeño; 0 para derivarlo de <see cref="hIcon"/>.</summary>
    public nint hIconSm;
}

/// <summary>KEYBDINPUT: un evento de teclado sintético para <c>SendInput</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    /// <summary>Tecla virtual.</summary>
    public ushort wVk;
    /// <summary>Código de escaneo.</summary>
    public ushort wScan;
    /// <summary>Banderas <c>KEYEVENTF_*</c> (soltar, tecla extendida...).</summary>
    public uint dwFlags;
    /// <summary>Marca de tiempo; 0 para que la ponga el sistema.</summary>
    public uint time;
    /// <summary>Dato extra que acompaña al evento.</summary>
    public nuint dwExtraInfo;
}

/// <summary>MOUSEINPUT: un evento de ratón sintético para <c>SendInput</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    /// <summary>Desplazamiento o posición horizontal, según <see cref="dwFlags"/>.</summary>
    public int dx;
    /// <summary>Desplazamiento o posición vertical, según <see cref="dwFlags"/>.</summary>
    public int dy;
    /// <summary>Giro de la rueda u otro dato, según el evento.</summary>
    public uint mouseData;
    /// <summary>Banderas <c>MOUSEEVENTF_*</c>.</summary>
    public uint dwFlags;
    /// <summary>Marca de tiempo; 0 para que la ponga el sistema.</summary>
    public uint time;
    /// <summary>Dato extra que acompaña al evento.</summary>
    public nuint dwExtraInfo;
}

/// <summary>HARDWAREINPUT: un evento de otro dispositivo de entrada para <c>SendInput</c>.</summary>
/// <remarks>Está para que la unión de <see cref="INPUT"/> tenga su tamaño real.</remarks>
[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    /// <summary>Mensaje que genera el dispositivo.</summary>
    public uint uMsg;
    /// <summary>Palabra baja del <c>lParam</c>.</summary>
    public ushort wParamL;
    /// <summary>Palabra alta del <c>lParam</c>.</summary>
    public ushort wParamH;
}

/// <summary>
/// INPUT for SendInput. The union starts at offset 8 on x64, which is the only architecture
/// this application is built for.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct INPUT
{
    /// <summary>Qué miembro de la unión vale: 0 ratón, 1 (<c>INPUT_KEYBOARD</c>) teclado, 2 hardware.</summary>
    [FieldOffset(0)]
    public uint type;

    /// <summary>El evento, si es de teclado.</summary>
    [FieldOffset(8)]
    public KEYBDINPUT ki;

    /// <summary>El evento, si es de ratón.</summary>
    [FieldOffset(8)]
    public MOUSEINPUT mi;

    /// <summary>El evento, si es de otro dispositivo.</summary>
    [FieldOffset(8)]
    public HARDWAREINPUT hi;
}

/// <summary>MONITORINFO: los dos rectángulos de un monitor y si es el principal.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    /// <summary>Tamaño de la estructura en bytes; hay que ponerlo antes de llamar a <c>GetMonitorInfo</c>.</summary>
    public int cbSize;

    /// <summary>El monitor entero.</summary>
    public RECT rcMonitor;

    /// <summary>Lo mismo sin la barra de tareas ni las barras acopladas.</summary>
    public RECT rcWork;

    /// <summary>Banderas; <see cref="User32.MONITORINFOF_PRIMARY"/> si es el monitor principal.</summary>
    public uint dwFlags;
}
