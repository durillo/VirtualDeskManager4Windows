using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.Views;

/// <summary>
/// Dibuja las zonas de un monitor mientras se arrastra una ventana con Mayús.
/// </summary>
/// <remarks>
/// Tiene que ser transparente al ratón. Windows está en mitad de un arrastre y le manda los
/// movimientos a la ventana que se arrastra; si esta superposición se los quedara, el arrastre se
/// cortaría en seco en cuanto apareciera. De eso se encarga <c>WS_EX_TRANSPARENT</c>, que hace que
/// el ratón la atraviese como si no estuviera.
///
/// Y no puede activarse nunca, o le robaría el foco a la ventana que el usuario tiene cogida.
/// </remarks>
public partial class ZoneOverlayWindow : Window
{
    private static readonly Brush ZoneFill = Frozen(Color.FromArgb(0x33, 0x3B, 0x82, 0xF6));
    private static readonly Brush ZoneEdge = Frozen(Color.FromArgb(0x88, 0x3B, 0x82, 0xF6));
    private static readonly Brush ActiveFill = Frozen(Color.FromArgb(0x77, 0x3B, 0x82, 0xF6));
    private static readonly Brush ActiveEdge = Frozen(Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA));
    private static readonly Brush Number = Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

    private readonly IWindowManager _windows;

    private WindowBounds _area;
    private double _scale = 1;

    /// <summary>Crea la superposición ya sin activarse y transparente al ratón, pero sin enseñarla.</summary>
    public ZoneOverlayWindow(IWindowManager windows)
    {
        InitializeComponent();

        _windows = windows;

        // El identificador se crea ya, antes de enseñarla nunca: los estilos hay que ponerlos
        // antes del primer Show o la superposición aparece un instante robando el ratón.
        nint handle = new WindowInteropHelper(this).EnsureHandle();

        _windows.SetNonActivating(handle, true);
        _windows.SetClickThrough(handle, true);
    }

    /// <summary>Enseña las zonas de un monitor, resaltando una.</summary>
    /// <param name="area">El área útil del monitor, en píxeles físicos.</param>
    /// <param name="zones">Las zonas ya calculadas, también en píxeles físicos.</param>
    /// <param name="active">Cuál está bajo el ratón, o -1.</param>
    public void ShowZones(WindowBounds area, IReadOnlyList<WindowBounds> zones, int active)
    {
        if (zones.Count == 0 || area.IsEmpty)
        {
            HideZones();
            return;
        }

        bool moved = _area != area;
        _area = area;

        if (moved)
        {
            _windows.PlaceOnTop(
                new WindowInteropHelper(this).Handle,
                area.X,
                area.Y,
                area.Width,
                area.Height);
        }

        // El DPI se lee después de colocarla, no antes: con dos pantallas a escalas distintas, el
        // de antes es el del monitor del que viene y dibujaría las zonas al tamaño equivocado.
        //
        // WPF puede tardar un fotograma en enterarse del cambio al cruzar de pantalla. No importa:
        // esto se repinta veinte veces por segundo mientras dura el arrastre, así que se corrige
        // antes de que se vea.
        _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

        if (_scale <= 0)
        {
            _scale = 1;
        }

        Draw(zones, active);

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>
    /// Esconde las zonas y olvida el monitor, para que la siguiente vez se vuelva a colocar aunque
    /// sea en el mismo.
    /// </summary>
    public void HideZones()
    {
        if (IsVisible)
        {
            Hide();
        }

        Surface.Children.Clear();
        _area = default;
    }

    private void Draw(IReadOnlyList<WindowBounds> zones, int active)
    {
        Surface.Children.Clear();

        for (int i = 0; i < zones.Count; i++)
        {
            WindowBounds zone = zones[i];
            bool isActive = i == active;

            Border box = new()
            {
                Width = Math.Max(1, zone.Width / _scale),
                Height = Math.Max(1, zone.Height / _scale),
                Background = isActive ? ActiveFill : ZoneFill,
                BorderBrush = isActive ? ActiveEdge : ZoneEdge,
                BorderThickness = new Thickness(isActive ? 3 : 1),
                CornerRadius = new CornerRadius(6),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    Foreground = Number,
                    FontSize = isActive ? 44 : 28,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = isActive ? 0.95 : 0.5
                }
            };

            Canvas.SetLeft(box, (zone.X - _area.X) / _scale);
            Canvas.SetTop(box, (zone.Y - _area.Y) / _scale);

            Surface.Children.Add(box);
        }
    }

    private static Brush Frozen(Color colour)
    {
        SolidColorBrush brush = new(colour);
        brush.Freeze();
        return brush;
    }
}
