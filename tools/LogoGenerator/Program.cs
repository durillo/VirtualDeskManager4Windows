using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LogoGenerator;

/// <summary>
/// Draws the application logo and writes it out in every format the project needs.
/// </summary>
/// <remarks>
/// The logo is generated and not drawn by hand so it stays reproducible: the shapes, the sizes
/// and the colours live in this file, and the binaries in <c>assets\</c> can be rebuilt from it
/// at any time. Run it by hand when the logo changes; it is not part of the build.
///
/// The mark is the same one the tray icon already used, promoted to a proper logo: four rounded
/// squares in a grid, the first one picked out in colour. It reads as "virtual desktops, one of
/// them active", and it survives being shrunk to sixteen pixels, which most drawings do not.
/// </remarks>
internal static class Program
{
    // Sizes Windows actually asks for: the tray and small lists use 16, Explorer 32 and 48,
    // Alt+Tab and the taskbar up to 64, and the Store style large icons 128 and 256.
    private static readonly int[] Sizes = { 16, 32, 48, 64, 128, 256 };

    internal static readonly Color Accent = Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);
    internal static readonly Color Dim = Color.FromArgb(0xFF, 0x47, 0x55, 0x69);

    private static readonly Color PausedAccent = Color.FromArgb(0xFF, 0x94, 0xA3, 0xB8);
    private static readonly Color PausedDim = Color.FromArgb(0xFF, 0x64, 0x74, 0x8B);

    private static int Main(string[] args)
    {
        string destino = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets");

        destino = Path.GetFullPath(destino);
        Directory.CreateDirectory(destino);

        Escribir(Path.Combine(destino, "vdesk.ico"), Accent, Dim);
        Escribir(Path.Combine(destino, "vdesk-pausado.ico"), PausedAccent, PausedDim);

        // Para el README y para cualquier sitio donde un .ico no sirva.
        foreach (int size in new[] { 256, 128 })
        {
            using Bitmap bitmap = Dibujar(size, Accent, Dim);
            bitmap.Save(Path.Combine(destino, $"vdesk-{size}.png"), ImageFormat.Png);
        }

        Console.WriteLine($"escrito en {destino}:");

        foreach (string fichero in Directory.GetFiles(destino).OrderBy(f => f))
        {
            Console.WriteLine($"  {Path.GetFileName(fichero),-24} {new FileInfo(fichero).Length,7} bytes");
        }

        return 0;
    }

    /// <summary>Dibuja el logo a un tamaño concreto.</summary>
    internal static Bitmap Dibujar(int size, Color accent, Color dim)
    {
        Bitmap bitmap = new(size, size, PixelFormat.Format32bppArgb);

        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Todo en proporción al tamaño, para que el dibujo sea el mismo a 16 y a 256.
        float margen = size * 0.08f;
        float hueco = size * 0.09f;
        float lado = (size - (margen * 2) - hueco) / 2f;

        // A tamaños pequeños el redondeo se come el cuadrado; se limita a algo que se vea.
        float radio = Math.Max(1f, lado * 0.22f);

        (float X, float Y, Color Colour)[] cuadrados =
        {
            (margen, margen, accent),
            (margen + lado + hueco, margen, dim),
            (margen, margen + lado + hueco, dim),
            (margen + lado + hueco, margen + lado + hueco, dim)
        };

        foreach ((float x, float y, Color colour) in cuadrados)
        {
            using GraphicsPath path = Redondeado(x, y, lado, lado, radio);
            using SolidBrush brush = new(colour);
            g.FillPath(brush, path);
        }

        return bitmap;
    }

    internal static GraphicsPath Redondeado(float x, float y, float width, float height, float radius)
    {
        float d = radius * 2;
        GraphicsPath path = new();

        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>
    /// Escribe un .ico con todos los tamaños dentro.
    /// </summary>
    /// <remarks>
    /// Los tamaños hasta 48 van como mapa de bits sin comprimir y los grandes como PNG. Windows
    /// admite PNG dentro de un .ico desde Vista, pero no todo lo que lee iconos lo hace igual de
    /// bien en los tamaños pequeños — el instalador entre ellos —, y a 48 píxeles el PNG tampoco
    /// ahorra nada que merezca el riesgo.
    /// </remarks>
    private static void Escribir(string ruta, Color accent, Color dim)
    {
        List<(int Size, byte[] Data, bool Png)> imagenes = new();

        foreach (int size in Sizes)
        {
            using Bitmap bitmap = Dibujar(size, accent, dim);

            if (size >= 64)
            {
                using MemoryStream png = new();
                bitmap.Save(png, ImageFormat.Png);
                imagenes.Add((size, png.ToArray(), true));
            }
            else
            {
                imagenes.Add((size, Dib(bitmap), false));
            }
        }

        using FileStream file = File.Create(ruta);
        using BinaryWriter w = new(file);

        // ICONDIR
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)imagenes.Count);

        // El primer byte de datos va detrás de la cabecera y de todas las entradas.
        int offset = 6 + (imagenes.Count * 16);

        foreach ((int size, byte[] data, _) in imagenes)
        {
            // 256 no cabe en un byte y se escribe como 0, que es lo que significa "256".
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write((uint)data.Length);
            w.Write((uint)offset);

            offset += data.Length;
        }

        foreach ((_, byte[] data, _) in imagenes)
        {
            w.Write(data);
        }
    }

    /// <summary>Mapa de bits de 32 bits con la máscara AND que el formato .ico exige detrás.</summary>
    private static byte[] Dib(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int xor = size * size * 4;

        // Cada fila de la máscara se alinea a 4 bytes; a estos tamaños sale justo.
        int filaMascara = ((size + 31) / 32) * 4;
        int and = filaMascara * size;

        using MemoryStream stream = new();
        using BinaryWriter w = new(stream);

        // BITMAPINFOHEADER. La altura va doblada porque la máscara cuenta como media imagen más.
        w.Write((uint)40);
        w.Write(size);
        w.Write(size * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write((uint)0);
        w.Write((uint)(xor + and));
        w.Write(0);
        w.Write(0);
        w.Write((uint)0);
        w.Write((uint)0);

        // Los colores van de abajo arriba, en BGRA.
        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = 0; x < size; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                w.Write(pixel.B);
                w.Write(pixel.G);
                w.Write(pixel.R);
                w.Write(pixel.A);
            }
        }

        // Máscara a cero: la transparencia ya la lleva el canal alfa.
        w.Write(new byte[and]);
        w.Flush();

        return stream.ToArray();
    }
}
