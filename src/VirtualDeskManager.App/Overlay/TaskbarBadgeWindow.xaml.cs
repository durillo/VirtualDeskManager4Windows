using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.Overlay;

/// <summary>Un distintivo ya colocado, en píxeles físicos de pantalla.</summary>
public sealed record PlacedBadge(string Text, WindowBounds Bounds);

/// <summary>
/// La capa que pinta los distintivos encima de la barra de tareas.
/// </summary>
/// <remarks>
/// Una ventana por monitor y no una por distintivo: la barra puede tener quince botones, y
/// quince ventanas por capa de superposición serían quince ventanas que colocar, mostrar y
/// esconder a la vez cada vez que se abre un programa.
///
/// Atraviesa las pulsaciones y no se activa nunca. Es lo que la hace aceptable: se pinta encima
/// de la barra de tareas, que es de otro proceso, y si robara un clic rompería el gesto más usado
/// del sistema.
/// </remarks>
public partial class TaskbarBadgeWindow : Window
{
    private readonly IWindowManager _windows;
    private readonly nint _handle;

    /// <summary>
    /// Crea la capa con su ventana nativa ya hecha, sin activarse nunca y dejando pasar los clics.
    /// No se ve hasta el primer <see cref="Draw"/>.
    /// </summary>
    public TaskbarBadgeWindow(IWindowManager windows)
    {
        InitializeComponent();

        _windows = windows;

        _handle = new WindowInteropHelper(this).EnsureHandle();

        _windows.SetNonActivating(_handle, true);
        _windows.SetClickThrough(_handle, true);
    }

    /// <summary>Coloca la ventana y pinta dentro los distintivos que le tocan.</summary>
    /// <param name="area">El rectángulo que abarca todos ellos, en píxeles físicos.</param>
    /// <param name="badges">Los distintivos ya colocados, con su posición en píxeles físicos de pantalla. Si no hay ninguno, la ventana se oculta.</param>
    /// <param name="settings">El aspecto de los distintivos: el color de fondo, del que sale también el del texto (negro o blanco).</param>
    public void Draw(WindowBounds area, IReadOnlyList<PlacedBadge> badges, TaskbarBadgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(badges);
        ArgumentNullException.ThrowIfNull(settings);

        if (badges.Count == 0 || area.Width <= 0 || area.Height <= 0)
        {
            Hide();
            return;
        }

        // PlaceOnTop y no MoveWindowVisible: aquella compensa el marco invisible de redimension,
        // que esta ventana no tiene, y ademas no reclama la banda de las ventanas superiores.
        // Esto se pinta sobre la barra de tareas, que es superior y se pone delante sola.
        //
        // Colocar antes de medir: el factor de escala de una ventana es el del monitor donde
        // esta, asi que preguntarlo mientras sigue en el limbo de -30000 da el del monitor
        // equivocado.
        _windows.PlaceOnTop(_handle, area.X, area.Y, area.Width, area.Height);

        double escala = VisualTreeHelper.GetDpi(this).DpiScaleX;

        if (escala <= 0)
        {
            escala = 1;
        }

        Surface.Children.Clear();

        Brush fondo = Fill(settings);
        Brush tinta = settings.ParsedColour.IsLight
            ? Brushes.Black
            : Brushes.White;

        foreach (PlacedBadge badge in badges)
        {
            double lado = badge.Bounds.Width / escala;

            Border marca = new()
            {
                Width = lado,
                Height = lado,
                Background = fondo,
                CornerRadius = new CornerRadius(lado / 2),
                Child = new TextBlock
                {
                    Text = badge.Text,
                    Foreground = tinta,
                    FontSize = Math.Max(7, lado * 0.62),
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };

            Canvas.SetLeft(marca, (badge.Bounds.X - area.X) / escala);
            Canvas.SetTop(marca, (badge.Bounds.Y - area.Y) / escala);

            Surface.Children.Add(marca);
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>Esconde la capa sin destruirla, que es lo normal entre repintados.</summary>
    public void Clear()
    {
        Surface.Children.Clear();
        Hide();
    }

    private static Brush Fill(TaskbarBadgeSettings settings)
    {
        RgbColour c = settings.ParsedColour;

        SolidColorBrush brocha = new(Color.FromArgb(
            (byte)Math.Clamp(settings.OpacityPercent * 255 / 100, 0, 255),
            c.R,
            c.G,
            c.B));

        brocha.Freeze();

        return brocha;
    }
}
