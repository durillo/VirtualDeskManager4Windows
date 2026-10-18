using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Desktops;

namespace VirtualDeskManager.App.Overlay;

/// <summary>
/// Una tira fina pegada a un borde de la consola desplegable: arrastrándola se cambia su tamaño.
/// </summary>
/// <remarks>
/// Propia y no el borde de la consola: la consola es una ventana de otro proceso (Windows Terminal)
/// a la que se le quita el marco, así que no tiene bordes que agarrar, y meterle algo dentro
/// exigiría inyectar código (§36). Como la chincheta, nunca se activa: pulsarla no le quita el foco
/// a la consola ni cuenta como «pasar a otra ventana».
/// </remarks>
public sealed class ConsoleResizeGrip : Window
{
    /// <summary>Grosor de la tira en unidades de WPF; en píxeles depende de la escala.</summary>
    public const double ThicknessDip = 6;

    // Casi transparente y no transparente del todo: con alfa cero, Windows deja pasar el ratón a lo
    // que haya debajo y la tira no se podría agarrar.
    private static readonly Brush Idle = Frozen(Color.FromArgb(0x01, 0, 0, 0));
    private static readonly Brush Hover = Frozen(Color.FromArgb(0xB0, 0x3A, 0x83, 0xF7));

    private readonly IWindowManager _windows;
    private nint _handle;
    private Point _start;
    private bool _dragging;

    /// <summary>Crea la tira de ese borde, con su ventana nativa ya hecha y sin enseñarla.</summary>
    public ConsoleResizeGrip(IWindowManager windows, ConsoleResizeEdge edge)
    {
        _windows = windows;
        Edge = edge;

        Title = "VirtualDeskManager.ConsoleResize";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Idle;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Left = -30000;
        Top = -30000;
        Width = ThicknessDip;
        Height = ThicknessDip;

        Cursor = edge switch
        {
            ConsoleResizeEdge.Left or ConsoleResizeEdge.Right => Cursors.SizeWE,
            ConsoleResizeEdge.Bottom => Cursors.SizeNS,
            ConsoleResizeEdge.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.SizeNWSE
        };

        _handle = new WindowInteropHelper(this).EnsureHandle();
        _windows.SetNonActivating(_handle, true);

        MouseEnter += (_, _) => Background = Hover;
        MouseLeave += (_, _) => Background = _dragging ? Hover : Idle;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += (_, _) => Finish();
        LostMouseCapture += (_, _) => Finish();
    }

    /// <summary>El borde de la consola al que va pegada.</summary>
    public ConsoleResizeEdge Edge { get; }

    /// <summary>Empieza a arrastrarse.</summary>
    public event EventHandler? DragStarted;

    /// <summary>Lo arrastrado desde que empezó, en píxeles físicos (horizontal, vertical).</summary>
    public event EventHandler<(int Dx, int Dy)>? Dragged;

    /// <summary>Se ha soltado.</summary>
    public event EventHandler? DragCompleted;

    /// <summary>Lado de la tira en píxeles físicos, con la escala de la pantalla.</summary>
    public int ThicknessPixels => (int)Math.Round(ThicknessDip * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>La pone en su sitio, por encima de todo y sin activarla.</summary>
    public void PlaceAt(LayoutRect rect)
    {
        // Primero se enseña y luego se coloca: WPF vuelve a aplicar el Left y el Top al enseñarla.
        if (!IsVisible)
        {
            Show();
        }

        _windows.PlaceOnTop(_handle, rect.Left, rect.Top, rect.Width, rect.Height);
    }

    /// <summary>La quita de la vista, salvo mientras se está arrastrando.</summary>
    public void HideGrip()
    {
        if (IsVisible && !_dragging)
        {
            Hide();
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        // En píxeles de pantalla, y no relativos a la tira: la tira se mueve mientras se arrastra.
        _start = PointToScreen(e.GetPosition(this));
        _dragging = CaptureMouse();

        if (_dragging)
        {
            DragStarted?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point now = PointToScreen(e.GetPosition(this));
        Dragged?.Invoke(this, ((int)Math.Round(now.X - _start.X), (int)Math.Round(now.Y - _start.Y)));
    }

    private void Finish()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Background = IsMouseOver ? Hover : Idle;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static Brush Frozen(Color colour)
    {
        SolidColorBrush brush = new(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>Olvida el identificador de ventana, que ya no vale.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _handle = 0;
        base.OnClosed(e);
    }
}
