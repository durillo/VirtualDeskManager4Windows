using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.Views.Enlace;

/// <summary>
/// Capturas de la ventana principal con datos de ejemplo, para enseñarla sin tocar la copia que
/// está en marcha ni el escritorio del usuario:
/// <c>VirtualDeskManager.exe --escaparate &lt;carpeta&gt;</c>.
/// </summary>
/// <remarks>
/// Abre la ventana fuera de la pantalla, dibuja cada sección en un PNG (claro y oscuro) y sale. No
/// arranca nada de la aplicación: ni los servicios, ni la bandeja, ni el control de una sola copia.
/// </remarks>
public static class Escaparate
{
    /// <summary>Hace las capturas en esa carpeta y vuelve.</summary>
    public static void Run(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        Loc.Instance.Use("es");

        foreach (bool oscuro in new[] { false, true })
        {
            string tema = oscuro ? "oscuro" : "claro";
            Func<VentanaVdeskViewModel, Window> crear = vm => new VentanaVdesk(vm, oscuro);

            Capturar(crear, Path.Combine(carpeta, $"{tema}-0-escritorios.png"), vm => vm.Seccion = SeccionVdesk.Escritorios);
        }
    }

    private static void Capturar(Func<VentanaVdeskViewModel, Window> crear, string fichero, Action<VentanaVdeskViewModel> preparar)
    {
        VentanaVdeskViewModel vm = new(Datos());
        preparar(vm);

        Window ventana = crear(vm);
        ventana.WindowStartupLocation = WindowStartupLocation.Manual;
        ventana.Left = -20000;
        ventana.Top = -20000;
        ventana.ShowActivated = false;
        ventana.ShowInTaskbar = false;

        ventana.Show();
        ventana.UpdateLayout();

        FrameworkElement contenido = (FrameworkElement)ventana.Content;
        double escala = VisualTreeHelper.GetDpi(ventana).DpiScaleX;
        RenderTargetBitmap foto = new(
            (int)(contenido.ActualWidth * escala), (int)(contenido.ActualHeight * escala),
            96 * escala, 96 * escala, PixelFormats.Pbgra32);

        // El fondo de la ventana, que el contenido no pinta.
        DrawingVisual fondo = new();
        using (DrawingContext dc = fondo.RenderOpen())
        {
            dc.DrawRectangle(ventana.Background, null, new Rect(0, 0, contenido.ActualWidth, contenido.ActualHeight));
        }

        foto.Render(fondo);
        foto.Render(contenido);

        PngBitmapEncoder png = new();
        png.Frames.Add(BitmapFrame.Create(foto));
        using (FileStream f = File.Create(fichero))
        {
            png.Save(f);
        }

        ventana.Close();
    }

    private static Ejemplo Datos() => new();

    private sealed class Ejemplo : IDatosVentana
    {
        public void Save() { }

        public void OpenSettings(string? section) { }

        public bool Dark { get; set; }

        public object? Settings => null;

        public IReadOnlyList<EscritorioDatos> Desktops()
        {
            static VentanaEnEscritorio V(int n, string programa, double x, double y, double w, double h) =>
                new(n, programa, programa, x, y, w, h, false);

            return new[]
            {
                new EscritorioDatos(Guid.NewGuid(), 1, "Trabajo", true, new[] { V(1, "Teams", 0.18, 0.45, 0.42, 0.4), V(2, "Outlook", 0.48, 0.2, 0.48, 0.55), V(3, "EXCEL", 0.04, 0.08, 0.55, 0.6) }),
                new EscritorioDatos(Guid.NewGuid(), 2, "Correo", false, new[] { V(4, "msedge", 0.5, 0.3, 0.45, 0.55), V(5, "olk", 0.05, 0.1, 0.65, 0.7) }),
                new EscritorioDatos(Guid.NewGuid(), 3, "Desarrollo", false, new[] { V(6, "msedge", 0.42, 0.55, 0.5, 0.4), V(7, "WindowsTerminal", 0.55, 0.15, 0.4, 0.45), V(8, "Code", 0.03, 0.05, 0.62, 0.75) }),
                new EscritorioDatos(Guid.NewGuid(), 4, "", false, new[] { V(9, "Spotify", 0.12, 0.12, 0.75, 0.7) }),
            };
        }

        public string? SwitchDesktop(Guid desktop) => null;

        public string? CreateDesktop() => null;

        public EliminacionEscritorio? DescribeRemoval(Guid desktop, int windows) => null;

        public string? RemoveDesktop(Guid desktop) => null;

        public string? RenameDesktop(Guid desktop, string name) => null;

        public string? MoveWindowToDesktop(nint window, Guid desktop) => null;
    }
}
