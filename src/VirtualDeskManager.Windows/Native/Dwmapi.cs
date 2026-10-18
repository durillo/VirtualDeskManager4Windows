using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Windows.Native;

/// <summary>
/// Declaraciones del gestor de ventanas del escritorio (DWM): atributos de ventana, miniaturas en
/// vivo, esquinas y borde.
/// </summary>
/// <remarks>
/// Las funciones nativas devuelven un HRESULT: 0 (S_OK) es exito y cualquier otro valor es fallo.
/// No usan GetLastError.
/// </remarks>
public static class Dwmapi
{
    /// <summary>DWMWA_CAPTION_BUTTON_BOUNDS: area of minimize, maximize and close, window relative.</summary>
    public const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;

    /// <summary>DWMWA_EXTENDED_FRAME_BOUNDS: el marco que de verdad se ve, sin la franja transparente.</summary>
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>DWM_TNP_RECTDESTINATION: <see cref="DWM_THUMBNAIL_PROPERTIES.rcDestination"/> is set.</summary>
    public const int DWM_TNP_RECTDESTINATION = 0x00000001;

    /// <summary>DWM_TNP_RECTSOURCE: <see cref="DWM_THUMBNAIL_PROPERTIES.rcSource"/> is set.</summary>
    public const int DWM_TNP_RECTSOURCE = 0x00000002;

    /// <summary>DWM_TNP_OPACITY: el campo de opacidad de <see cref="DWM_THUMBNAIL_PROPERTIES"/> trae valor.</summary>
    public const int DWM_TNP_OPACITY = 0x00000004;

    /// <summary>DWM_TNP_VISIBLE: el campo de visibilidad de <see cref="DWM_THUMBNAIL_PROPERTIES"/> trae valor.</summary>
    public const int DWM_TNP_VISIBLE = 0x00000008;

    /// <summary>DWM_TNP_SOURCECLIENTAREAONLY: el campo que limita la miniatura al area cliente trae valor.</summary>
    public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    /// <summary>Lee un atributo de ventana del DWM que es un rectangulo.</summary>
    /// <remarks>
    /// Devuelve un HRESULT (0 si va bien). Los rectangulos vienen en pixeles fisicos, sin escalar
    /// por PPP: <see cref="DWMWA_EXTENDED_FRAME_BOUNDS"/> en coordenadas de pantalla y
    /// <see cref="DWMWA_CAPTION_BUTTON_BOUNDS"/> relativo a la ventana. Para atributos enteros esta
    /// <see cref="DwmGetWindowAttributeInt"/>.
    /// </remarks>
    /// <param name="hwnd">Ventana.</param>
    /// <param name="attribute">Atributo <c>DWMWA_*</c>, por ejemplo <see cref="DWMWA_EXTENDED_FRAME_BOUNDS"/>.</param>
    /// <param name="value">El rectangulo leido.</param>
    /// <param name="size">Tamano en bytes de <paramref name="value"/>.</param>
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);

    /// <summary>
    /// Starts showing <paramref name="source"/> inside <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// The relation is live: the compositor keeps the thumbnail in step with the real window on
    /// its own, at no cost to this process, and it goes on doing so while the source sits on
    /// another virtual desktop, where the shell has it cloaked. Measured on build 26200: a
    /// minimised window still answers with its last composed frame rather than with nothing.
    ///
    /// The destination must be a window of this process, and it must not be layered: a
    /// <c>WS_EX_LAYERED</c> destination is accepted with S_OK and then never composes anything at
    /// all, taking the rest of the window's own painting down with it.
    /// </remarks>
    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    /// <summary>Deja de mostrar una miniatura registrada con <see cref="DwmRegisterThumbnail"/> y libera su asa.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(nint thumbnail);

    /// <summary>Cambia donde y como se pinta una miniatura: destino, recorte, opacidad, visibilidad.</summary>
    /// <remarks>
    /// Solo se aplican los campos marcados en <c>dwFlags</c> con las constantes <c>DWM_TNP_*</c>.
    /// Devuelve un HRESULT.
    /// </remarks>
    [DllImport("dwmapi.dll")]
    public static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DWM_THUMBNAIL_PROPERTIES properties);

    /// <summary>Size of the source window, needed to give the thumbnail the right shape.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out SIZE size);

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE: si Windows 11 redondea las esquinas.</summary>
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>Lo que decida Windows.</summary>
    public const int DWMWCP_DEFAULT = 0;

    /// <summary>Esquinas cuadradas.</summary>
    public const int DWMWCP_DONOTROUND = 1;

    /// <summary>Cambia un atributo de ventana del DWM que es un entero, como <see cref="DWMWA_WINDOW_CORNER_PREFERENCE"/>.</summary>
    /// <remarks>Devuelve un HRESULT (0 si va bien).</remarks>
    [DllImport("dwmapi.dll", SetLastError = false)]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>La misma funcion para los atributos que son un entero y no un rectangulo.</summary>
    [DllImport("dwmapi.dll", SetLastError = false, EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeInt(nint hwnd, int attribute, out int value, int size);

    /// <summary>
    /// Como tiene las esquinas esta ventana, o null si Windows no lo sabe decir.
    /// </summary>
    /// <remarks>
    /// Medido en este equipo: Visual Studio Code pide redondeo grande (2) y Teams se deja lo que
    /// diga Windows (0). En Windows 11 las dos acaban redondeadas.
    /// </remarks>
    public static int? TryGetCornerPreference(nint hwnd)
    {
        try
        {
            return DwmGetWindowAttributeInt(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, out int preference, sizeof(int)) == 0
                ? preference
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pide a Windows 11 una forma de esquinas para la ventana (<see cref="DWMWCP_DEFAULT"/>,
    /// <see cref="DWMWCP_DONOTROUND"/>...). Falso si Windows no lo acepta.
    /// </summary>
    public static bool TrySetCornerPreference(nint hwnd, int preference)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE: la barra de título de la ventana, en oscuro.</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Pone la barra de título de la ventana en oscuro o en claro, sin tocar sus botones ni lo que
    /// hacen. Falso si Windows no lo acepta.
    /// </summary>
    public static bool TrySetDarkTitleBar(nint hwnd, bool dark)
    {
        int valor = dark ? 1 : 0;

        try
        {
            return DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref valor, sizeof(int)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>DWMWA_BORDER_COLOR: el color del filo de un pixel que Windows 11 pinta alrededor.</summary>
    public const int DWMWA_BORDER_COLOR = 34;

    /// <summary>Que lo elija Windows.</summary>
    public const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    /// <summary>Sin filo.</summary>
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    /// <summary>La misma <c>DwmSetWindowAttribute</c> para los atributos que son un color (<c>COLORREF</c>).</summary>
    /// <remarks>Devuelve un HRESULT (0 si va bien).</remarks>
    [DllImport("dwmapi.dll", SetLastError = false, EntryPoint = "DwmSetWindowAttribute")]
    public static extern int DwmSetWindowAttributeColour(nint hwnd, int attribute, ref uint value, int size);

    /// <summary>
    /// Quita o repone el filo de un pixel del borde de la ventana.
    /// </summary>
    /// <remarks>
    /// Medido en el equipo del usuario, en la junta de dos ventanas encajadas: los pixeles del
    /// borde salian en #7C7D7B y #707270 sobre un contenido de #121314, o sea una raya gris clara
    /// de dos pixeles —uno por ventana— sobre fondo oscuro. Quitando este atributo pasaron a
    /// #202020 y #181818, indistinguibles del contenido.
    /// </remarks>
    public static bool TrySetBorderVisible(nint hwnd, bool visible)
    {
        try
        {
            uint value = visible ? DWMWA_COLOR_DEFAULT : DWMWA_COLOR_NONE;
            return DwmSetWindowAttributeColour(hwnd, DWMWA_BORDER_COLOR, ref value, sizeof(uint)) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// El marco visible de la ventana, sin la franja transparente de agarre; null si el DWM no lo
    /// da o sale vacio.
    /// </summary>
    /// <remarks>
    /// Medido en este equipo: siete pixeles a izquierda, derecha y abajo en Visual Studio Code,
    /// Edge, Teams y la terminal; cero arriba. Algunas ventanas devuelven cero por los cuatro
    /// lados, y entonces no hay nada que corregir.
    /// </remarks>
    public static WindowBounds? TryGetVisibleBounds(nint hwnd)
    {
        try
        {
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds, Marshal.SizeOf<RECT>()) != 0)
            {
                return null;
            }

            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;

            return width <= 0 || height <= 0
                ? null
                : new WindowBounds(bounds.Left, bounds.Top, width, height);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Width in pixels that the system caption buttons occupy on the right of the caption, or
    /// null when the desktop manager does not draw them.
    /// </summary>
    /// <remarks>
    /// Measured on Windows 11: three buttons take 146 pixels over a 23 pixel caption, which is
    /// far more than a naive "three squares" guess. Applications drawing their own caption
    /// (Spotify, WhatsApp, Microsoft Store, Visual Studio Code) answer with an empty rectangle,
    /// and the caller then falls back to an estimate.
    /// </remarks>
    public static int? TryGetCaptionButtonsWidth(nint hwnd)
    {
        try
        {
            if (DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, out RECT bounds, Marshal.SizeOf<RECT>()) != 0)
            {
                return null;
            }

            int width = bounds.Width;
            return width > 0 ? width : null;
        }
        catch (Exception)
        {
            // dwmapi is always present on Windows 10 and 11, but a missing export must not
            // take the button down with it.
            return null;
        }
    }
}
