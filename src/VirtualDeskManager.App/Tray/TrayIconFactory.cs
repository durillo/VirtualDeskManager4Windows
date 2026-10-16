using System;
using System.IO;
using System.Reflection;

namespace VirtualDeskManager.App.Tray;

/// <summary>
/// Hands out the tray icon: the application logo, and a washed out version of it while the rules
/// are paused.
/// </summary>
/// <remarks>
/// The icons are read from the embedded .ico files rather than drawn pixel by pixel here, which
/// is what this class used to do. One drawing in one place, <c>tools\LogoGenerator</c>, means the
/// tray, the taskbar and the installer cannot end up showing three different marks.
///
/// Reading from a stream also keeps the old property worth keeping: no GDI handle is created
/// behind our back, unlike <c>Bitmap.GetHicon</c>, which leaks one on every call.
/// </remarks>
public static class TrayIconFactory
{
    private const string Normal = "VirtualDeskManager.vdesk.ico";
    private const string Paused = "VirtualDeskManager.vdesk-pausado.ico";

    /// <summary>
    /// El logotipo leído del <c>.ico</c> incrustado, o su versión apagada si las reglas están en
    /// pausa. Quien lo recibe es quien lo libera.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si el icono no está incrustado en el ensamblado.</exception>
    public static System.Drawing.Icon Create(bool paused = false)
    {
        string name = paused ? Paused : Normal;

        using Stream? stream = typeof(TrayIconFactory).Assembly.GetManifestResourceStream(name);

        if (stream is null)
        {
            // Sin icono no hay bandeja, y sin bandeja no hay forma de salir de la aplicación, así
            // que esto vale más como fallo ruidoso en tiempo de compilación que como null.
            throw new InvalidOperationException(
                $"El recurso incrustado '{name}' no está en el ensamblado. Genera los iconos con tools\\LogoGenerator.");
        }

        // El .ico lleva dentro de 16 a 256 píxeles; Windows escoge el que le toque según el DPI.
        return new System.Drawing.Icon(stream);
    }

    /// <summary>
    /// El icono con el número del escritorio actual encima.
    /// </summary>
    /// <remarks>
    /// Sólo el número: en un icono de bandeja, que el sistema acaba enseñando a 16 píxeles, no
    /// cabe un nombre y forzarlo daría una mancha ilegible. El nombre del escritorio va en el
    /// tooltip, que sí tiene sitio de sobra.
    ///
    /// Se dibuja a 32 píxeles y se deja que Windows reduzca, en vez de dibujar directamente a 16:
    /// el texto sale mejor así, y en pantallas con escala Windows pide el tamaño grande.
    ///
    /// Un número de dos cifras se dibuja más estrecho para que quepa. A partir de ahí no se
    /// intenta: quien tenga cien escritorios abiertos tiene otros problemas.
    /// </remarks>
    /// <param name="number">Número del escritorio, empezando en 1.</param>
    /// <param name="paused">Partir del icono de pausa en vez del normal, cuando Vdesk está en pausa.</param>
    public static System.Drawing.Icon CreateWithNumber(int number, bool paused = false)
    {
        const int Size = 32;

        using System.Drawing.Icon baseIcon = Create(paused);
        using System.Drawing.Bitmap canvas = new(Size, Size);

        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(canvas))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using (System.Drawing.Icon scaled = new(baseIcon, Size, Size))
            {
                graphics.DrawIcon(scaled, new System.Drawing.Rectangle(0, 0, Size, Size));
            }

            string text = number > 0 ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";

            // Un disco liso detrás del número. El logotipo queda tapado, y es a propósito: a los
            // 16 píxeles a los que Windows acaba enseñando esto, un número sobre los cuatro
            // cuadros de colores no se lee, y el número es lo único que se viene a mirar.
            //
            // El disco va en el azul del logotipo y no en negro, para que el icono siga
            // reconociéndose como el de este programa aunque la marca no se vea: en la bandeja
            // se distingue por el color mucho antes que por la forma.
            System.Drawing.Color plateColour = paused
                ? System.Drawing.Color.FromArgb(235, 0x64, 0x74, 0x8B)
                : System.Drawing.Color.FromArgb(235, 0x3B, 0x82, 0xF6);

            using System.Drawing.SolidBrush plate = new(plateColour);
            graphics.FillEllipse(plate, 0, 0, Size - 1, Size - 1);

            float points = text.Length >= 2 ? 15f : 20f;

            using System.Drawing.Font font = new(
                "Segoe UI",
                points,
                System.Drawing.FontStyle.Bold,
                System.Drawing.GraphicsUnit.Pixel);

            using System.Drawing.StringFormat format = new()
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center
            };

            using System.Drawing.SolidBrush ink = new(System.Drawing.Color.White);

            graphics.DrawString(text, font, ink, new System.Drawing.RectangleF(0, 0, Size, Size), format);
        }

        // GetHicon crea un manejador de GDI que hay que devolver a mano: sin el DestroyIcon se
        // filtraría uno en cada cambio de escritorio, que son muchos a lo largo de un día.
        nint handle = canvas.GetHicon();

        try
        {
            using System.Drawing.Icon temporary = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool DestroyIcon(nint handle);
    }
}
