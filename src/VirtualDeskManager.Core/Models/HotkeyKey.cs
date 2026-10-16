namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Key of a global hotkey. Values are Win32 virtual key codes and the names follow the WPF
/// <c>System.Windows.Input.Key</c> naming so that hotkeys.json stays readable.
/// </summary>
/// <remarks>
/// The specification (section 6) shows <c>Key</c> typed as the WPF <c>Key</c> enum. Using it here
/// would force a WPF reference into Core and break architecture rule 1, so the enum is mirrored.
/// </remarks>
public enum HotkeyKey
{
    /// <summary>Ninguna tecla: el atajo o la segunda combinación no están puestos.</summary>
    None = 0x00,

    /// <summary>Retroceso.</summary>
    Backspace = 0x08,
    /// <summary>Tabulador.</summary>
    Tab = 0x09,
    /// <summary>Intro.</summary>
    Enter = 0x0D,
    /// <summary>Pausa.</summary>
    Pause = 0x13,
    /// <summary>Bloqueo de mayúsculas.</summary>
    CapsLock = 0x14,
    /// <summary>Escape.</summary>
    Escape = 0x1B,
    /// <summary>Barra espaciadora.</summary>
    Space = 0x20,
    /// <summary>Retroceder página (Re Pág).</summary>
    PageUp = 0x21,
    /// <summary>Avanzar página (Av Pág).</summary>
    PageDown = 0x22,
    /// <summary>Fin.</summary>
    End = 0x23,
    /// <summary>Inicio.</summary>
    Home = 0x24,
    /// <summary>Flecha izquierda.</summary>
    Left = 0x25,
    /// <summary>Flecha arriba.</summary>
    Up = 0x26,
    /// <summary>Flecha derecha.</summary>
    Right = 0x27,
    /// <summary>Flecha abajo.</summary>
    Down = 0x28,
    /// <summary>Imprimir pantalla.</summary>
    PrintScreen = 0x2C,
    /// <summary>Insertar.</summary>
    Insert = 0x2D,
    /// <summary>Suprimir.</summary>
    Delete = 0x2E,

    /// <summary>El 0 de la fila de números (no el del teclado numérico).</summary>
    D0 = 0x30,
    /// <summary>El 1 de la fila de números.</summary>
    D1 = 0x31,
    /// <summary>El 2 de la fila de números.</summary>
    D2 = 0x32,
    /// <summary>El 3 de la fila de números.</summary>
    D3 = 0x33,
    /// <summary>El 4 de la fila de números.</summary>
    D4 = 0x34,
    /// <summary>El 5 de la fila de números.</summary>
    D5 = 0x35,
    /// <summary>El 6 de la fila de números.</summary>
    D6 = 0x36,
    /// <summary>El 7 de la fila de números.</summary>
    D7 = 0x37,
    /// <summary>El 8 de la fila de números.</summary>
    D8 = 0x38,
    /// <summary>El 9 de la fila de números.</summary>
    D9 = 0x39,

    /// <summary>Letra A.</summary>
    A = 0x41,
    /// <summary>Letra B.</summary>
    B = 0x42,
    /// <summary>Letra C.</summary>
    C = 0x43,
    /// <summary>Letra D.</summary>
    D = 0x44,
    /// <summary>Letra E.</summary>
    E = 0x45,
    /// <summary>Letra F.</summary>
    F = 0x46,
    /// <summary>Letra G.</summary>
    G = 0x47,
    /// <summary>Letra H.</summary>
    H = 0x48,
    /// <summary>Letra I.</summary>
    I = 0x49,
    /// <summary>Letra J.</summary>
    J = 0x4A,
    /// <summary>Letra K.</summary>
    K = 0x4B,
    /// <summary>Letra L.</summary>
    L = 0x4C,
    /// <summary>Letra M.</summary>
    M = 0x4D,
    /// <summary>Letra N.</summary>
    N = 0x4E,
    /// <summary>Letra O.</summary>
    O = 0x4F,
    /// <summary>Letra P.</summary>
    P = 0x50,
    /// <summary>Letra Q.</summary>
    Q = 0x51,
    /// <summary>Letra R.</summary>
    R = 0x52,
    /// <summary>Letra S.</summary>
    S = 0x53,
    /// <summary>Letra T.</summary>
    T = 0x54,
    /// <summary>Letra U.</summary>
    U = 0x55,
    /// <summary>Letra V.</summary>
    V = 0x56,
    /// <summary>Letra W.</summary>
    W = 0x57,
    /// <summary>Letra X.</summary>
    X = 0x58,
    /// <summary>Letra Y.</summary>
    Y = 0x59,
    /// <summary>Letra Z.</summary>
    Z = 0x5A,

    /// <summary>0 del teclado numérico (con Bloq Num activado).</summary>
    NumPad0 = 0x60,
    /// <summary>1 del teclado numérico.</summary>
    NumPad1 = 0x61,
    /// <summary>2 del teclado numérico.</summary>
    NumPad2 = 0x62,
    /// <summary>3 del teclado numérico.</summary>
    NumPad3 = 0x63,
    /// <summary>4 del teclado numérico.</summary>
    NumPad4 = 0x64,
    /// <summary>5 del teclado numérico.</summary>
    NumPad5 = 0x65,
    /// <summary>6 del teclado numérico.</summary>
    NumPad6 = 0x66,
    /// <summary>7 del teclado numérico.</summary>
    NumPad7 = 0x67,
    /// <summary>8 del teclado numérico.</summary>
    NumPad8 = 0x68,
    /// <summary>9 del teclado numérico.</summary>
    NumPad9 = 0x69,
    /// <summary>* del teclado numérico.</summary>
    Multiply = 0x6A,
    /// <summary>+ del teclado numérico.</summary>
    Add = 0x6B,
    /// <summary>- del teclado numérico.</summary>
    Subtract = 0x6D,
    /// <summary>Separador decimal del teclado numérico.</summary>
    Decimal = 0x6E,
    /// <summary>/ del teclado numérico.</summary>
    Divide = 0x6F,

    /// <summary>F1.</summary>
    F1 = 0x70,
    /// <summary>F2.</summary>
    F2 = 0x71,
    /// <summary>F3.</summary>
    F3 = 0x72,
    /// <summary>F4.</summary>
    F4 = 0x73,
    /// <summary>F5.</summary>
    F5 = 0x74,
    /// <summary>F6.</summary>
    F6 = 0x75,
    /// <summary>F7.</summary>
    F7 = 0x76,
    /// <summary>F8.</summary>
    F8 = 0x77,
    /// <summary>F9.</summary>
    F9 = 0x78,
    /// <summary>F10.</summary>
    F10 = 0x79,
    /// <summary>F11.</summary>
    F11 = 0x7A,
    /// <summary>F12.</summary>
    F12 = 0x7B,

    /// <summary>Bloqueo numérico.</summary>
    NumLock = 0x90,
    /// <summary>Bloqueo de desplazamiento.</summary>
    ScrollLock = 0x91,

    /*
     * Las teclas OEM completas, y no cinco sueltas como antes.
     *
     * Cuál es cuál depende de la distribución del teclado, así que el nombre no dice gran cosa:
     * en un teclado español, medido el 2026-09-15, Oem5 es la tecla de º ª \ (la de debajo de
     * Escape, la que Quake usaba para su consola) y Oem3 es la Ñ, aunque su nombre histórico sea
     * OemTilde porque en un teclado estadounidense esa misma posición da la ~.
     *
     * Faltaban justamente éstas, y no era un detalle: el editor de atajos descartaba en silencio
     * cualquier tecla que no estuviera aquí, de modo que pulsar º no hacía absolutamente nada y
     * parecía que el editor estuviera roto.
     */
    /// <summary>VK_OEM_1: la tecla de <c>; :</c> en un teclado estadounidense. En otros, la que caiga en esa posición.</summary>
    Oem1 = 0xBA,
    /// <summary>VK_OEM_PLUS: la tecla del <c>+</c> de la fila principal, en cualquier distribución.</summary>
    OemPlus = 0xBB,
    /// <summary>VK_OEM_COMMA: la tecla de la coma, en cualquier distribución.</summary>
    OemComma = 0xBC,
    /// <summary>VK_OEM_MINUS: la tecla del <c>-</c> de la fila principal, en cualquier distribución.</summary>
    OemMinus = 0xBD,
    /// <summary>VK_OEM_PERIOD: la tecla del punto, en cualquier distribución.</summary>
    OemPeriod = 0xBE,
    /// <summary>VK_OEM_2: la tecla de <c>/ ?</c> en un teclado estadounidense.</summary>
    Oem2 = 0xBF,

    /// <summary>Ñ en un teclado español; la tecla de la ~ en uno estadounidense.</summary>
    OemTilde = 0xC0,

    /// <summary>VK_OEM_4: la tecla de <c>[ {</c> en un teclado estadounidense.</summary>
    Oem4 = 0xDB,

    /// <summary>La tecla de º ª \ en un teclado español: la de debajo de Escape.</summary>
    Oem5 = 0xDC,

    /// <summary>VK_OEM_6: la tecla de <c>] }</c> en un teclado estadounidense.</summary>
    Oem6 = 0xDD,
    /// <summary>VK_OEM_7: la tecla de las comillas <c>' "</c> en un teclado estadounidense.</summary>
    Oem7 = 0xDE,
    /// <summary>VK_OEM_8: sin uso fijo; depende de la distribución y muchas no la tienen.</summary>
    Oem8 = 0xDF,

    /// <summary>La tecla de más que tienen los teclados europeos, entre Shift y la Z.</summary>
    Oem102 = 0xE2
}
