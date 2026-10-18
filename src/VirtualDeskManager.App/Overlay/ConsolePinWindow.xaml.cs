using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.Overlay;

/// <summary>
/// La pestaña con la chincheta que cuelga de la consola desplegable: un clic la fija para que no
/// se recoja sola, y otro la suelta.
/// </summary>
/// <remarks>
/// Es una ventana propia y no un botón dentro de la consola por lo mismo que el botón de la barra
/// de título: la consola es una ventana de otro proceso (Windows Terminal) y meterle nada dentro
/// exigiría inyectar código, que §36 prohíbe. Nunca se activa, así que pulsarla no le quita el
/// foco a la consola y no cuenta como «pasar a otra ventana».
/// </remarks>
public partial class ConsolePinWindow : Window
{
    /// <summary>Lado de la pestaña en unidades de WPF; en píxeles depende de la escala.</summary>
    private const double SizeDip = 26;

    private static readonly Brush PinnedFill = Frozen(Color.FromRgb(0x3A, 0x83, 0xF7));
    private static readonly Brush LooseFill = Frozen(Color.FromArgb(0xE0, 0x20, 0x20, 0x20));
    private static readonly Brush LooseHover = Frozen(Color.FromArgb(0xF0, 0x38, 0x38, 0x38));
    private static readonly Brush Edge = Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    private static readonly Brush White = Frozen(Colors.White);

    /// <summary>Chincheta (Pin) y chincheta rellena (PinFill) de Segoe Fluent Icons.</summary>
    private const string PinGlyph = "";
    private const string PinnedGlyph = "";

    private readonly IWindowManager _windows;
    private nint _handle;
    private bool _pinned;
    private bool _hovering;

    /// <summary>Crea la pestaña con su ventana nativa ya hecha, sin enseñarla.</summary>
    /// <param name="windows">Para colocarla sin pasar por Win32 desde la interfaz.</param>
    public ConsolePinWindow(IWindowManager windows)
    {
        InitializeComponent();

        _windows = windows;
        _handle = new WindowInteropHelper(this).EnsureHandle();
        _windows.SetNonActivating(_handle, true);

        Root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        Root.MouseEnter += (_, _) => Paint(hovering: true);
        Root.MouseLeave += (_, _) => Paint(hovering: false);

        Paint(hovering: false);
    }

    /// <summary>Se ha pulsado la chincheta.</summary>
    public event EventHandler? Clicked;

    /// <summary>Lado de la pestaña en píxeles físicos, con la escala de la pantalla.</summary>
    public int SizePixels => (int)Math.Round(SizeDip * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>Pinta la chincheta como fijada o como suelta.</summary>
    public void SetPinned(bool pinned)
    {
        _pinned = pinned;
        Paint(_hovering);
    }

    /// <summary>La pone en su sitio, por encima de todo y sin activarla.</summary>
    public void PlaceAt(LayoutRect rect)
    {
        // Primero se enseña y luego se coloca: WPF vuelve a aplicar el Left y el Top del XAML al
        // enseñar la ventana, y la mandaría a su aparcamiento fuera de la pantalla.
        if (!IsVisible)
        {
            Show();
        }

        _windows.PlaceOnTop(_handle, rect.Left, rect.Top, rect.Width, rect.Height);
    }

    /// <summary>La quita de la vista.</summary>
    public void HideTab()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Paint(bool hovering)
    {
        _hovering = hovering;

        Root.Background = _pinned ? PinnedFill : hovering ? LooseHover : LooseFill;
        Root.BorderBrush = Edge;
        Glyph.Foreground = White;
        Glyph.Text = _pinned ? PinnedGlyph : PinGlyph;

        Root.ToolTip = Loc.Get(_pinned ? "Console.Unpin" : "Console.Pin");
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
