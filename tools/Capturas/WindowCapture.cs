using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>
/// La foto de una ventana en PNG, con <c>PrintWindow</c>: se dibuja a sí misma en un mapa de bits,
/// así que sale aunque otra la tape, y no se toca la pantalla del usuario. Con el equipo bloqueado
/// o la sesión desconectada Windows no dibuja nada y devuelve nulo.
/// </summary>
/// <remarks>
/// Vivía en VirtualDeskManager.Windows.Share, que se fue con el enlace entre equipos (MiRed). Sólo
/// la usa esta herramienta, así que está aquí.
/// </remarks>
internal static class WindowCapture
{
    private const uint PW_CLIENTONLY = 0x1;
    private const uint PW_RENDERFULLCONTENT = 0x2;

    public static byte[]? CapturePng(nint window)
    {
        if (!GetClientRect(window, out RECT cliente) || cliente.Right - cliente.Left <= 0 || cliente.Bottom - cliente.Top <= 0)
        {
            return null;
        }

        int ancho = cliente.Right - cliente.Left;
        int alto = cliente.Bottom - cliente.Top;
        byte[] pixeles;

        nint pantalla = GetDC(0);
        nint memoria = CreateCompatibleDC(pantalla);
        nint mapa = 0;
        nint anterior = 0;

        try
        {
            BITMAPINFOHEADER cabecera = new()
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = ancho,
                biHeight = -alto,
                biPlanes = 1,
                biBitCount = 32
            };

            mapa = CreateDIBSection(pantalla, ref cabecera, 0, out nint bits, 0, 0);

            if (mapa == 0 || bits == 0)
            {
                return null;
            }

            anterior = SelectObject(memoria, mapa);

            if (!PrintWindow(window, memoria, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
            {
                return null;
            }

            pixeles = new byte[ancho * 4 * alto];
            Marshal.Copy(bits, pixeles, 0, pixeles.Length);
        }
        finally
        {
            if (anterior != 0)
            {
                SelectObject(memoria, anterior);
            }

            if (mapa != 0)
            {
                DeleteObject(mapa);
            }

            DeleteDC(memoria);
            ReleaseDC(0, pantalla);
        }

        PngBitmapEncoder codificador = new();
        codificador.Frames.Add(BitmapFrame.Create(BitmapSource.Create(ancho, alto, 96, 96, PixelFormats.Bgr32, null, pixeles, ancho * 4)));

        using MemoryStream salida = new();
        codificador.Save(salida);
        return salida.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(nint hWnd, nint hdcBlt, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);
}
