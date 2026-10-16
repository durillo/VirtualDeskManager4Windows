using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.Views.Settings;

/// <summary>
/// «Cargando…» para todo lo que espera a la red: una barra en movimiento, qué se espera, que
/// depende de la conexión y cuánto lleva. Pedido por el usuario el 2026-10-09: una lista en blanco
/// mientras contesta otro equipo parece rota, y la gente se desespera.
/// </summary>
/// <remarks>
/// Se pone encima de la lista que se está llenando (en la misma celda de un <see cref="Grid"/>) y
/// la tapa mientras <see cref="IsActive"/>. Con <see cref="Compact"/> es una línea suelta para una
/// fila de ajustes. Todo sitio nuevo que espere a la red debería usarlo.
/// </remarks>
public sealed class Loading : Border
{
    /// <summary>Si se está esperando. Sin ello no se ve ni ocupa sitio.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(Loading), new PropertyMetadata(false, (d, _) => ((Loading)d).Refresh()));

    /// <summary>Qué se espera («Preguntando a «Portátil»…»). Sin él, «Cargando…».</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Loading), new PropertyMetadata("", (d, _) => ((Loading)d).Refresh()));

    /// <summary>En una línea, sin tapar nada: para una fila de ajustes.</summary>
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(Loading), new PropertyMetadata(false, (d, _) => ((Loading)d).Build()));

    /// <summary>A partir de cuándo se dice cuánto lleva: antes sólo distrae.</summary>
    private static readonly TimeSpan ShowElapsedAfter = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _elapsed = new() { TextAlignment = TextAlignment.Center };
    private DateTime _since;

    /// <summary>Apagado hasta que se encienda.</summary>
    public Loading()
    {
        Visibility = Visibility.Collapsed;
        _timer.Tick += (_, _) => ShowElapsed();
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => Refresh();
        Build();
    }

    /// <inheritdoc cref="IsActiveProperty"/>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <inheritdoc cref="TextProperty"/>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc cref="CompactProperty"/>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Lo enciende con este texto, o lo apaga. Para quien lo maneja desde el código.</summary>
    public void Show(bool active, string text = "")
    {
        Text = text;
        IsActive = active;
    }

    private void Build()
    {
        foreach (TextBlock t in new[] { _title, _hint, _elapsed })
        {
            (t.Parent as Panel)?.Children.Remove(t);
        }

        ProgressBar barra = new() { IsIndeterminate = true, Height = 4 };

        if (Compact)
        {
            // Una línea: la barra, que depende de la conexión y cuánto lleva. Lo que se espera ya
            // lo dice la fila.
            Background = null;
            Padding = new Thickness(0);
            barra.Width = 120;
            barra.VerticalAlignment = VerticalAlignment.Center;
            _elapsed.Margin = new Thickness(8, 0, 0, 0);
            _elapsed.VerticalAlignment = VerticalAlignment.Center;
            _elapsed.TextWrapping = TextWrapping.Wrap;
            _elapsed.MaxWidth = 340;
            _elapsed.SetResourceReference(StyleProperty, "Secondary");

            StackPanel linea = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            linea.Children.Add(barra);
            linea.Children.Add(_elapsed);
            Child = linea;
            return;
        }

        // Encima de la lista, tapándola: lo que hubiera es de antes y ya no vale.
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        Padding = new Thickness(24);
        barra.Width = 220;
        barra.Margin = new Thickness(0, 0, 0, 14);
        _title.SetResourceReference(StyleProperty, "Body");
        _title.FontWeight = FontWeights.SemiBold;
        _hint.SetResourceReference(StyleProperty, "Secondary");
        _hint.Margin = new Thickness(0, 4, 0, 0);
        _elapsed.SetResourceReference(StyleProperty, "Secondary");
        _elapsed.Margin = new Thickness(0, 8, 0, 0);

        StackPanel centro = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
        centro.Children.Add(barra);
        centro.Children.Add(_title);
        centro.Children.Add(_hint);
        centro.Children.Add(_elapsed);
        Child = centro;
    }

    private void Refresh()
    {
        _title.Text = Text is { Length: > 0 } t ? t : Loc.Get("Loading.Title");
        _hint.Text = Loc.Get("Loading.Hint");

        if (IsActive)
        {
            if (Visibility != Visibility.Visible)
            {
                _since = DateTime.UtcNow;
                Visibility = Visibility.Visible;
            }

            ShowElapsed();

            _timer.Start();
        }
        else
        {
            Visibility = Visibility.Collapsed;
            _timer.Stop();
        }
    }

    private void ShowElapsed()
    {
        TimeSpan lleva = DateTime.UtcNow - _since;
        string cuanto = lleva < ShowElapsedAfter ? "" : Loc.Format("Loading.Elapsed", (int)lleva.TotalSeconds);

        // En una línea no cabe el aviso aparte: va delante de lo que lleva.
        _elapsed.Text = Compact ? (cuanto.Length > 0 ? Loc.Get("Loading.Hint") + " " + cuanto : Loc.Get("Loading.Hint")) : cuanto;
    }
}
