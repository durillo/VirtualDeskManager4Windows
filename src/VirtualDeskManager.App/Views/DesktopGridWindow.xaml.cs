using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Overlay;

namespace VirtualDeskManager.App.Views;

/// <summary>
/// Every virtual desktop at once: the screen split into one vertical zone per desktop, each
/// holding live thumbnails of the windows that live there, which can be dragged from one zone to
/// another to move the window between desktops.
/// </summary>
/// <remarks>
/// Two facts about DWM thumbnails decide the whole shape of this window, and both were measured
/// rather than assumed (build 26200):
///
/// The compositor paints a thumbnail over everything the destination window draws underneath it,
/// so no caption, border or veil can be placed on top of one. Chrome goes around the thumbnail,
/// never over it, and a thumbnail is dimmed through its own opacity rather than by covering it.
///
/// A layered destination window composes nothing at all, so this window has no transparency and
/// no <c>Opacity</c>. It is opaque, and that is not negotiable.
///
/// Everything is laid out by <see cref="DesktopGridLayout"/> in physical pixels and placed on a
/// canvas by hand. Letting WPF arrange the tiles and then chasing its rectangles with DWM would
/// mean two layout passes that agree only by luck.
/// </remarks>
public partial class DesktopGridWindow : Window
{
    /// <summary>How far the pointer travels before a press becomes a drag rather than a click.</summary>
    private const double DragThreshold = 6;

    /// <summary>Minimised windows show their last frame, so they are dimmed to say so.</summary>
    private const byte MinimisedOpacity = 140;

    private static readonly Brush ZoneBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x24));
    private static readonly Brush ZoneBorder = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x3A));
    private static readonly Brush CurrentZoneBorder = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush DropZoneBorder = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush FrameBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x4A));
    private static readonly Brush HoverFrameBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xF0));
    private static readonly Brush DimTextBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xAD));
    private static readonly Brush CloseBackground = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));
    private static readonly Brush CloseGlyph = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>Lo que se le da a la ventana para cerrarse antes de volver a dibujar la rejilla.</summary>
    private const int CloseSettleMilliseconds = 250;

    /// <summary>Ancho de la columna «+ Nuevo escritorio», en unidades de WPF.</summary>
    private const double NewDesktopColumnDip = 150;

    /// <summary>Tregua tras cerrar una ventana antes de hacer caso a perder el primer plano.</summary>
    private const int DeactivateGraceMilliseconds = 600;

    private readonly IVirtualDesktopService _desktops;
    private readonly IWindowManager _windows;
    private readonly IAppLogger _logger;
    private readonly uint _ownProcessId;

    /// <summary>El menú de cada ventana, el mismo del botón de la barra de título. Nulo: sin menú.</summary>
    private readonly Overlay.WindowMenu? _menu;

    /// <summary>Mientras el menú de una ventana está abierto, perder el primer plano no cierra la rejilla.</summary>
    private bool _menuOpen;

    /// <summary>Crear, renombrar y eliminar escritorios. Nulo: la vista no ofrece nada de eso.</summary>
    private readonly AccionesEscritorios? _acciones;

    /// <summary>Mientras hay un diálogo propio delante (confirmar, nombre), perder el primer plano tampoco cierra.</summary>
    private bool _dialogOpen;

    /// <summary>La «×» de eliminar de cada escritorio: su zona, dónde está y su dibujo.</summary>
    private readonly List<(int Zone, LayoutRect Rect, Border Button)> _removeButtons = new();

    /// <summary>Cuántas ventanas tiene cada escritorio, para contarlo antes de eliminarlo.</summary>
    private readonly Dictionary<Guid, int> _windowCount = new();

    /// <summary>La columna «+ Nuevo escritorio»; vacía si no hay.</summary>
    private LayoutRect _newDesktop;
    private Border? _newDesktopPanel;

    private readonly List<GridEntry> _entries = new();
    private readonly List<ZoneVisual> _zones = new();

    private DpiScale _dpi = new(1, 1);
    private nint _foregroundBefore;

    private GridEntry? _pressed;
    private Point _pressedAt;
    private bool _dragging;

    /// <summary>El soltar que viene de pulsar la cruz no es un clic en el fondo.</summary>
    private bool _closedFromCross;

    /// <summary>
    /// Hasta cuando se ignora que la rejilla pierda el primer plano.
    /// </summary>
    /// <remarks>
    /// Cerrar una ventana puede mover el foco un instante, y la rejilla se oculta en cuanto deja
    /// de estar delante. Sin esta tregua, cerrar una ventana cerraba la rejilla por el otro lado.
    /// </remarks>
    private DateTime _ignoreDeactivateUntil = DateTime.MinValue;
    private int _dropZone = -1;

    /// <summary>Crea la ventana sin enseñarla; <see cref="DesktopGridService"/> se la queda y la reutiliza.</summary>
    /// <param name="desktops">Los escritorios que se reparten la pantalla, y quien mueve ventanas entre ellos.</param>
    /// <param name="windows">Las ventanas que se enseñan y sus miniaturas.</param>
    /// <param name="ownProcessId">El proceso de Vdesk: sus ventanas se dejan fuera para que la rejilla no se vea a sí misma.</param>
    /// <param name="logger">Dónde apuntar los problemas.</param>
    /// <param name="menu">El menú de cada ventana con el botón derecho; sin él, no hay menú.</param>
    /// <param name="acciones">Crear, renombrar y eliminar escritorios desde la vista; sin ellas, no se ofrece.</param>
    public DesktopGridWindow(
        IVirtualDesktopService desktops,
        IWindowManager windows,
        uint ownProcessId,
        IAppLogger? logger = null,
        Overlay.WindowMenu? menu = null,
        AccionesEscritorios? acciones = null)
    {
        InitializeComponent();

        _acciones = acciones;

        _desktops = desktops;
        _windows = windows;
        _ownProcessId = ownProcessId;
        _logger = logger ?? NullAppLogger.Instance;
        _menu = menu;

        if (_menu is not null)
        {
            // Lo que se cambia desde el menú (mover, fijar) cambia la rejilla: se vuelve a pintar.
            _menu.AfterAction = () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsOpen)
                {
                    Build();
                }
            }));
        }

        FlowDirection = Loc.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        Surface.MouseLeftButtonDown += OnMouseDown;
        Surface.MouseMove += OnMouseMove;
        Surface.MouseLeftButtonUp += OnMouseUp;
        Surface.MouseRightButtonUp += OnRightButtonUp;

        PreviewKeyDown += OnKeyDown;
        Deactivated += OnDeactivated;

        // Shutdown closes it for real; the thumbnails have to go with it.
        Closed += (_, _) => Teardown();
    }

    /// <summary>True while the overview is on screen.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Builds the overview against the desktops and windows as they are right now, and shows it.
    /// </summary>
    /// <remarks>
    /// Everything here is measured in the tens of milliseconds: enumerating the windows is a
    /// handful of Win32 calls each, and registering a thumbnail asks the compositor to reuse a
    /// surface it already has rather than to take a picture. That is what allows the view to be
    /// up, with live content, on the same keystroke that asked for it.
    /// </remarks>
    public void ShowGrid()
    {
        if (IsOpen)
        {
            return;
        }

        _foregroundBefore = _windows.GetForegroundWindow();

        PlaceOverScreen();

        // The handle has to exist before a thumbnail can be registered against it, and the size
        // has to be final before anything is laid out inside it.
        Show();

        _dpi = VisualTreeHelper.GetDpi(this);

        Build();

        IsOpen = true;
        Activate();
        Focus();

        // Con detalle a proposito: si la vista no llega a verse, lo unico que se puede mirar
        // despues es esta linea, y "no aparecio" tiene demasiadas causas distintas como para
        // distinguirlas sin saber si la ventana estaba visible, de que tamano y con cuantas
        // miniaturas dentro.
        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", nameof(ShowGrid)),
            ("What", "DesktopGrid"),
            ("Visible", IsVisible),
            ("Active", IsActive),
            ("Bounds", $"{Left},{Top} {ActualWidth}x{ActualHeight}"),
            ("Dpi", _dpi.DpiScaleX),
            ("Zones", _zones.Count),
            ("Windows", _entries.Count),
            ("Foreground", _windows.GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(this).Handle));
    }

    /// <summary>Closes the overview and gives the foreground back to where it was.</summary>
    public void HideGrid()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Teardown();
        Hide();

        // Without this the focus lands wherever Windows feels like, which after a full screen
        // window is usually the desktop rather than what the user was working in.
        if (_foregroundBefore != 0 && _windows.IsWindow(_foregroundBefore))
        {
            _windows.ActivateWindow(_foregroundBefore);
        }

        _foregroundBefore = 0;
    }

    /// <summary>
    /// Creates the window handle ahead of time, so the first time the shortcut is pressed there
    /// is no XAML to parse and no window to create.
    /// </summary>
    public void Prewarm()
    {
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>
    /// Covers the whole primary screen, taskbar included.
    /// </summary>
    /// <remarks>
    /// The screen and not the working area: an overview that leaves the taskbar showing reads as
    /// a window sitting on the desktop rather than as a mode the user is in.
    /// </remarks>
    private void PlaceOverScreen()
    {
        WindowState = WindowState.Normal;
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    private void Build()
    {
        Teardown();

        IReadOnlyList<VirtualDesktopInfo> desktops = _desktops.GetDesktops();

        if (desktops.Count == 0)
        {
            return;
        }

        VirtualDesktopInfo? current = _desktops.GetCurrentDesktop();

        // One pass over the windows, sorted into the desktop each one belongs to. A window that
        // belongs to no single desktop is pinned to all of them and goes into its own strip.
        Dictionary<Guid, List<WindowInfo>> byDesktop = new();
        List<WindowInfo> pinned = new();

        foreach (VirtualDesktopInfo desktop in desktops)
        {
            byDesktop[desktop.Id] = new List<WindowInfo>();
        }

        foreach (WindowInfo window in _windows.GetTopLevelWindows())
        {
            // This very overlay is a top level window with a title: without this it would show a
            // thumbnail of itself inside itself.
            if (window.ProcessId == _ownProcessId || !IsWorthShowing(window))
            {
                continue;
            }

            Guid? desktopId = _desktops.GetWindowDesktopId(window.Handle);

            if (desktopId is null || !byDesktop.TryGetValue(desktopId.Value, out List<WindowInfo>? bucket))
            {
                pinned.Add(window);
                continue;
            }

            bucket.Add(window);
        }

        // Thumbnails first: the shape of each window is what the layout needs, and only the
        // compositor knows it.
        List<List<GridEntry>> columns = desktops
            .Select(desktop => byDesktop[desktop.Id].Select(w => CreateEntry(w, desktop.Id)).ToList())
            .ToList();

        List<GridEntry> pinnedEntries = pinned.Select(w => CreateEntry(w, null)).ToList();

        DesktopGridLayoutOptions options = new()
        {
            NewDesktopColumnWidth = _acciones?.PuedeCrear == true ? ToPixelsX(NewDesktopColumnDip) : 0
        };

        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(
            ToPixelsX(ActualWidth),
            ToPixelsY(ActualHeight),
            columns.Select(c => (IReadOnlyList<double>)c.Select(e => e.AspectRatio).ToList()).ToList(),
            pinnedEntries.Select(e => e.AspectRatio).ToList(),
            options);

        bool canRemove = _acciones?.PuedeEliminar == true && desktops.Count > 1;

        if (!layout.PinnedStrip.IsEmpty)
        {
            DrawZone(layout.PinnedStrip, Loc.Get("Grid.Pinned"), isCurrent: false, desktopId: null, index: -1);
            PlaceCells(pinnedEntries, layout.PinnedCells);
        }

        for (int index = 0; index < layout.Columns.Count; index++)
        {
            GridColumnLayout column = layout.Columns[index];
            VirtualDesktopInfo desktop = desktops[index];

            bool isCurrent = current is not null && current.Id == desktop.Id;

            string name = desktop.HasName ? desktop.Name : Loc.Format("Overlay.DesktopNumber", desktop.Number);

            DrawZone(
                new LayoutRect(column.Header.Left, column.Header.Top, column.Header.Width, column.Header.Height + column.Body.Height),
                name,
                isCurrent,
                desktop.Id,
                index);

            PlaceCells(columns[index], column.Cells);

            _windowCount[desktop.Id] = columns[index].Count;

            if (canRemove)
            {
                DrawRemoveButton(_zones.Count - 1, column.Header);
            }
        }

        if (!layout.NewDesktop.IsEmpty)
        {
            DrawNewDesktop(layout.NewDesktop);
        }

        DrawHint();
    }

    /// <summary>
    /// Whether a window is one the user would think of as an open application.
    /// </summary>
    /// <remarks>
    /// <c>Windows.UI.Core.CoreWindow</c> is the one that has to go. On this build the shell keeps
    /// a visible, titled window of that class alive at all times for the text input experience,
    /// and it passes every ordinary test for a real window: it is not a tool window, it has a
    /// title, and it belongs to no virtual desktop, so it lands in the pinned strip and sits
    /// there as a blue rectangle nobody asked for. Genuine Store applications do not use it:
    /// they are hosted in <c>ApplicationFrameWindow</c>, which is left alone.
    ///
    /// Checked rather than assumed, because the obvious guess was wrong: that window carries
    /// neither <c>WS_EX_NOACTIVATE</c> nor <c>WS_EX_TOOLWINDOW</c>, so neither style would have
    /// caught it.
    /// </remarks>
    private static bool IsWorthShowing(WindowInfo window) =>
        !string.Equals(window.ClassName, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal);

    private GridEntry CreateEntry(WindowInfo window, Guid? desktopId)
    {
        DwmThumbnail? thumbnail = DwmThumbnail.Register(
            new System.Windows.Interop.WindowInteropHelper(this).Handle,
            window.Handle,
            _logger);

        return new GridEntry(window, desktopId, thumbnail);
    }

    private void PlaceCells(IReadOnlyList<GridEntry> entries, IReadOnlyList<GridCellLayout> cells)
    {
        for (int index = 0; index < entries.Count && index < cells.Count; index++)
        {
            GridEntry entry = entries[index];
            GridCellLayout cell = cells[index];

            entry.Thumbnail = cell.Thumbnail;
            entry.Caption = cell.Caption;
            entry.Close = cell.Close;

            entry.Frame = DrawFrame(cell.Thumbnail);
            entry.Label = DrawCaption(cell.Caption, entry.Window);
            entry.CloseButton = DrawCloseButton(cell.Close);

            entry.PlaceThumbnail();

            _entries.Add(entry);
        }
    }

    private void DrawZone(LayoutRect rect, string title, bool isCurrent, Guid? desktopId, int index)
    {
        Border panel = new()
        {
            Width = ToDipX(rect.Width),
            Height = ToDipY(rect.Height),
            Background = ZoneBrush,
            BorderBrush = isCurrent ? CurrentZoneBorder : ZoneBorder,
            BorderThickness = new Thickness(isCurrent ? 2 : 1),
            CornerRadius = new CornerRadius(10)
        };

        Canvas.SetLeft(panel, ToDipX(rect.Left));
        Canvas.SetTop(panel, ToDipY(rect.Top));
        Surface.Children.Add(panel);

        TextBlock header = new()
        {
            Text = title,
            Foreground = isCurrent ? TextBrush : DimTextBrush,
            FontSize = 15,
            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            // Sitio para la «×» de eliminar, a la derecha.
            MaxWidth = Math.Max(0, ToDipX(rect.Width) - 64)
        };

        Canvas.SetLeft(header, ToDipX(rect.Left) + 14);
        Canvas.SetTop(header, ToDipY(rect.Top) + 12);
        Surface.Children.Add(header);

        if (desktopId is not null)
        {
            _zones.Add(new ZoneVisual(index, desktopId.Value, rect, panel, isCurrent));
        }
    }

    /// <summary>
    /// The border that marks a window. It is drawn just outside the thumbnail, never over it,
    /// because anything over it would be invisible.
    /// </summary>
    private Border DrawFrame(LayoutRect thumbnail)
    {
        Border frame = new()
        {
            Width = ToDipX(thumbnail.Width) + 6,
            Height = ToDipY(thumbnail.Height) + 6,
            BorderBrush = FrameBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(frame, ToDipX(thumbnail.Left) - 3);
        Canvas.SetTop(frame, ToDipY(thumbnail.Top) - 3);
        Surface.Children.Add(frame);

        return frame;
    }


    /// <summary>
    /// La cruz de cerrar, arriba a la derecha de la miniatura.
    /// </summary>
    /// <remarks>
    /// Justo encima del borde superior de la miniatura y nunca dentro: DWM compone las miniaturas
    /// por encima de todo lo que pinte la ventana, asi que una cruz dibujada ahi dentro no se
    /// veria. La distribucion ya deja ese hueco reservado.
    ///
    /// No captura el raton: el clic lo resuelve el mismo sitio que el resto de la rejilla,
    /// comparando coordenadas, para que arrastrar desde cualquier parte siga funcionando igual.
    /// </remarks>
    private Border? DrawCloseButton(LayoutRect close)
    {
        if (close.IsEmpty)
        {
            return null;
        }

        Border button = new()
        {
            Width = ToDipX(close.Width),
            Height = ToDipY(close.Height),
            Background = CloseBackground,
            CornerRadius = new CornerRadius(4),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "\u2715",
                Foreground = CloseGlyph,
                FontSize = ToDipY(close.Height) * 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        Canvas.SetLeft(button, ToDipX(close.Left));
        Canvas.SetTop(button, ToDipY(close.Top));
        Surface.Children.Add(button);

        return button;
    }

    /// <summary>
    /// La «×» que elimina un escritorio, a la derecha de su cabecera.
    /// </summary>
    /// <remarks>
    /// Escondida hasta que el ratón pasa por ese escritorio: siempre a la vista se confundiría con
    /// la cruz roja que cierra una ventana, y eliminar un escritorio es otra cosa. Como la de las
    /// ventanas, no captura el ratón: el clic se resuelve comparando coordenadas.
    /// </remarks>
    private void DrawRemoveButton(int zone, LayoutRect header)
    {
        int side = Math.Max(16, header.Height - 16);
        LayoutRect rect = new(header.Right - 10 - side, header.Top + (header.Height - side) / 2, side, side);

        Border button = new()
        {
            Width = ToDipX(rect.Width),
            Height = ToDipY(rect.Height),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Hidden,
            Child = new TextBlock
            {
                Text = "\u2715",
                Foreground = DimTextBrush,
                FontSize = ToDipY(rect.Height) * 0.45,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        Canvas.SetLeft(button, ToDipX(rect.Left));
        Canvas.SetTop(button, ToDipY(rect.Top));
        Surface.Children.Add(button);

        _removeButtons.Add((zone, rect, button));
    }

    /// <summary>La columna «+ Nuevo escritorio», a la derecha de todas.</summary>
    private void DrawNewDesktop(LayoutRect rect)
    {
        _newDesktop = rect;

        StackPanel content = new()
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        content.Children.Add(new TextBlock
        {
            Text = "+",
            FontSize = 34,
            Foreground = DimTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        content.Children.Add(new TextBlock
        {
            Text = Loc.Get("Vdesk.NewDesktop"),
            FontSize = 13,
            Foreground = DimTextBrush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(0, ToDipX(rect.Width) - 16),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _newDesktopPanel = new Border
        {
            Width = ToDipX(rect.Width),
            Height = ToDipY(rect.Height),
            BorderBrush = ZoneBorder,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Child = content
        };

        Canvas.SetLeft(_newDesktopPanel, ToDipX(rect.Left));
        Canvas.SetTop(_newDesktopPanel, ToDipY(rect.Top));
        Surface.Children.Add(_newDesktopPanel);
    }

    /// <summary>Enseña la «×» del escritorio bajo el ratón (en rojo si está encima) y resalta la columna «+».</summary>
    private void ShowDesktopControls(Point position)
    {
        int x = ToPixelsX(position.X);
        int y = ToPixelsY(position.Y);
        int zone = ZoneAt(position);

        foreach ((int z, LayoutRect rect, Border button) in _removeButtons)
        {
            bool over = rect.Contains(x, y);
            button.Visibility = z == zone ? Visibility.Visible : Visibility.Hidden;
            button.Background = over ? CloseBackground : Brushes.Transparent;
            ((TextBlock)button.Child).Foreground = over ? CloseGlyph : DimTextBrush;
        }

        if (_newDesktopPanel is not null)
        {
            _newDesktopPanel.BorderBrush = _newDesktop.Contains(x, y) ? CurrentZoneBorder : ZoneBorder;
        }
    }

    /// <summary>La zona cuya «×» de eliminar está bajo ese punto, o -1.</summary>
    private int RemoveButtonAt(Point position)
    {
        int x = ToPixelsX(position.X);
        int y = ToPixelsY(position.Y);

        foreach ((int zone, LayoutRect rect, _) in _removeButtons)
        {
            if (rect.Contains(x, y))
            {
                return zone;
            }
        }

        return -1;
    }

    /// <summary>Si ese punto cae en la columna «+ Nuevo escritorio».</summary>
    private bool IsOnNewDesktop(Point position) =>
        !_newDesktop.IsEmpty && _newDesktop.Contains(ToPixelsX(position.X), ToPixelsY(position.Y));

    /// <summary>Crea un escritorio al final y vuelve a enseñar la vista con él ya dentro.</summary>
    private void CreateDesktop()
    {
        if (_acciones is null)
        {
            return;
        }

        RunOutside(_acciones.Crear);
    }

    /// <summary>
    /// Elimina un escritorio. Sus ventanas pasan al de al lado; sólo se pregunta si tiene ventanas
    /// o alguna automatización lo usa.
    /// </summary>
    private void RemoveDesktop(ZoneVisual zone)
    {
        if (_acciones is null)
        {
            return;
        }

        EliminacionEscritorio? info = _acciones.Describir(zone.DesktopId, _windowCount.GetValueOrDefault(zone.DesktopId));

        if (info is null)
        {
            Tell(Loc.Get("Desktops.RemoveLast"));
            return;
        }

        if (info.Pregunta() is { } question && !Ask(question))
        {
            return;
        }

        Guid desktop = zone.DesktopId;
        RunOutside(() => _acciones.Eliminar(desktop));
    }

    /// <summary>Le cambia el nombre a un escritorio, preguntándolo.</summary>
    private void RenameDesktop(ZoneVisual zone, string current)
    {
        if (_acciones is null)
        {
            return;
        }

        string? name;
        _dialogOpen = true;

        try
        {
            name = TextPromptWindow.Ask(this, Loc.Get("Vdesk.Rename"), Loc.Get("Vdesk.DesktopName"), current);
        }
        finally
        {
            _dialogOpen = false;
        }

        if (name is not null && _acciones.Renombrar(zone.DesktopId, name) is { } error)
        {
            Tell(error);
        }

        Build();

        if (IsOpen && !HasForeground())
        {
            Activate();
        }
    }

    /// <summary>
    /// Hace algo que cambia de escritorio por el camino (crear, eliminar) con la vista quitada, y
    /// la vuelve a enseñar al terminar con lo que haya quedado.
    /// </summary>
    /// <remarks>
    /// Con la vista puesta, el cambio de escritorio se la llevaría a medias: es una ventana más y
    /// vive en un escritorio. Quitarla y volver a ponerla es lo que deja verla entera y al día. Lo
    /// que tenía delante el usuario antes de abrirla se conserva para devolvérselo al cerrar.
    /// </remarks>
    private void RunOutside(Func<string?> action)
    {
        nint before = _foregroundBefore;
        _foregroundBefore = 0;

        HideGrid();
        string? error = action();
        ShowGrid();

        _foregroundBefore = before;

        if (error is not null)
        {
            Tell(error);
        }
    }

    /// <summary>Pregunta algo encima de la vista sin que ésta se cierre por perder el primer plano.</summary>
    private bool Ask(string question)
    {
        _dialogOpen = true;

        try
        {
            return MessageBox.Show(this, question, AppVersion.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                   == MessageBoxResult.Yes;
        }
        finally
        {
            _dialogOpen = false;

            if (IsOpen && !HasForeground())
            {
                Activate();
            }
        }
    }

    /// <summary>Cuenta algo encima de la vista sin que ésta se cierre.</summary>
    private void Tell(string message)
    {
        _dialogOpen = true;

        try
        {
            MessageBox.Show(this, message, AppVersion.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _dialogOpen = false;

            if (IsOpen && !HasForeground())
            {
                Activate();
            }
        }
    }

    /// <summary>
    /// Cierra la ventana y quita su hueco de la rejilla.
    /// </summary>
    /// <remarks>
    /// Se pide el cierre educado, el mismo que el aspa de la propia ventana, asi que un programa
    /// con cambios sin guardar preguntara lo que tenga que preguntar. Por eso aqui no se pide
    /// confirmacion: seria preguntar dos veces por lo mismo, y en el unico caso que importa lo
    /// pregunta quien sabe lo que se pierde.
    /// </remarks>
    private void CloseEntry(GridEntry entry)
    {
        // Antes de pedir el cierre, no despues: la ventana puede irse tan deprisa que Windows
        // ponga otra delante mientras esta linea todavia no se ha ejecutado.
        _ignoreDeactivateUntil = DateTime.UtcNow.AddMilliseconds(DeactivateGraceMilliseconds);

        OperationResult result = _windows.CloseWindow(entry.Window.Handle);

        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", "CloseFromGrid"),
            ("Window", entry.Window.Handle),
            ("Process", entry.Window.ProcessName),
            ("Status", result.Status));

        if (!result.IsSuccess)
        {
            return;
        }

        // Se rehace la rejilla entera y no solo se borra el hueco: al irse una ventana, las demas
        // de esa columna reparten su sitio y salen mas grandes, que es lo que uno espera ver.
        //
        // Con un respiro antes, porque WM_CLOSE es una peticion, no una orden: un programa con
        // cambios sin guardar abre su pregunta y sigue ahi. Rehacer en el acto lo dibujaria como
        // si nada hubiera pasado, y rehacer un poco despues deja ver lo que de verdad ha ocurrido.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(async () =>
            {
                await Task.Delay(CloseSettleMilliseconds).ConfigureAwait(true);

                if (IsVisible)
                {
                    Build();
                }
            }));
    }

    private TextBlock DrawCaption(LayoutRect caption, WindowInfo window)
    {
        string text = string.IsNullOrWhiteSpace(window.Title) ? window.ProcessName : window.Title;

        if (window.IsMinimized)
        {
            text = Loc.Format("Grid.Minimised", text);
        }

        TextBlock label = new()
        {
            Text = text,
            Foreground = window.IsMinimized ? DimTextBrush : TextBrush,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Width = Math.Max(0, ToDipX(caption.Width) - 8),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(label, ToDipX(caption.Left) + 4);
        Canvas.SetTop(label, ToDipY(caption.Top) + 4);
        Surface.Children.Add(label);

        return label;
    }

    private void DrawHint()
    {
        TextBlock hint = new()
        {
            Text = Loc.Get("Grid.Hint"),
            Foreground = DimTextBrush,
            FontSize = 12,
            IsHitTestVisible = false
        };

        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Canvas.SetLeft(hint, (ActualWidth - hint.DesiredSize.Width) / 2);
        Canvas.SetTop(hint, ActualHeight - hint.DesiredSize.Height - 6);
        Surface.Children.Add(hint);
    }

    private void Teardown()
    {
        foreach (GridEntry entry in _entries)
        {
            entry.Dispose();
        }

        _entries.Clear();
        _zones.Clear();
        _removeButtons.Clear();
        _windowCount.Clear();
        _newDesktop = default;
        _newDesktopPanel = null;
        Surface.Children.Clear();

        _pressed = null;
        _dragging = false;
        _dropZone = -1;

        // OverrideCursor is application wide: left set, the hand would follow the user into
        // whatever they went back to.
        Mouse.OverrideCursor = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            e.Handled = true;
            HideGrid();
        }
    }

    /// <remarks>
    /// Anything that takes the foreground away means the user has moved on. An overview that
    /// stays behind another window is a full screen surprise waiting to happen.
    /// </remarks>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!IsOpen || _dragging || _menuOpen || _dialogOpen)
        {
            return;
        }

        // Acabamos de cerrar una ventana: puede que el foco se haya ido un instante al vecino que
        // Windows pone delante en su lugar. Eso no significa que el usuario se haya ido, y cerrar
        // la rejilla ahí le obligaría a reabrirla para cerrar la siguiente.
        if (DateTime.UtcNow < _ignoreDeactivateUntil)
        {
            // Pero tampoco se puede dar por hecho: si lo que ha tomado el primer plano es un
            // cuadro de "¿guardar los cambios?", la rejilla estaría tapándolo a pantalla completa.
            // Se comprueba al acabar la tregua, cuando ya se sabe dónde ha acabado el foco.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(async () =>
                {
                    await Task.Delay(DeactivateGraceMilliseconds).ConfigureAwait(true);

                    if (IsOpen && !_dragging && !HasForeground())
                    {
                        HideGrid();
                    }
                }));

            return;
        }

        _logger.Debug(LogEvents.DesktopOperation, ("Operation", "DesktopGridDeactivated"));
        HideGrid();
    }

    /// <summary>
    /// Botón derecho: sobre una ventana, su menú; en cualquier otro sitio, cerrar la vista, que es
    /// lo que hacía siempre.
    /// </summary>
    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        Point position = e.GetPosition(Surface);
        GridEntry? entry = _menu is null ? null : HitTest(position);

        if (entry is null)
        {
            // En un escritorio, aunque sea en un hueco: su menú (ir, nombre, eliminar).
            int zone = ZoneAt(position);

            if (zone >= 0 && _acciones is not null)
            {
                e.Handled = true;
                OpenDesktopMenu(_zones[zone]);
                return;
            }

            HideGrid();
            return;
        }

        e.Handled = true;
        OpenMenu(entry);
    }

    private void OpenMenu(GridEntry entry)
    {
        nint handle = entry.Window.Handle;

        ContextMenu menu = new()
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false,
            FlowDirection = FlowDirection
        };

        menu.Items.Add(_menu!.AlwaysOnTop(handle, entry.Window.IsTopMost));
        menu.Items.Add(_menu.ShowOnAllDesktops(handle));
        menu.Items.Add(_menu.MoveToDesktop(handle));
        menu.Items.Add(new Separator());

        // Cerrar es lo único que puede perder trabajo: va por el mismo camino que la cruz, que
        // pide el cierre por las buenas y deja que la aplicación pregunte si guarda.
        MenuItem cerrar = new() { Header = Loc.Get("Overlay.Close") };
        cerrar.Click += (_, _) => CloseEntry(entry);
        menu.Items.Add(cerrar);

        menu.Closed += (_, _) =>
        {
            _menuOpen = false;

            // El menú se ha llevado el foco un momento: si se cerró sin elegir nada, la rejilla
            // sigue siendo lo de delante.
            if (IsOpen && !HasForeground())
            {
                Activate();
            }
        };

        _menuOpen = true;
        Highlight(entry);
        menu.IsOpen = true;
    }

    /// <summary>El menú de un escritorio: ir a él, cambiarle el nombre o eliminarlo.</summary>
    private void OpenDesktopMenu(ZoneVisual zone)
    {
        VirtualDesktopInfo? desktop = _desktops.GetDesktops().FirstOrDefault(d => d.Id == zone.DesktopId);

        if (desktop is null || _acciones is null)
        {
            return;
        }

        ContextMenu menu = new()
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false,
            FlowDirection = FlowDirection
        };

        MenuItem go = new() { Header = Loc.Get("Vdesk.GoToDesktop"), IsEnabled = !zone.IsCurrent };
        go.Click += (_, _) => GoToDesktop(zone.DesktopId);
        menu.Items.Add(go);

        MenuItem rename = new() { Header = Loc.Get("Vdesk.Rename") };
        rename.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => RenameDesktop(zone, desktop.Name)));
        menu.Items.Add(rename);

        menu.Items.Add(new Separator());

        // Separado y en rojo: es lo único que quita algo. Con un solo escritorio no se puede, y la
        // entrada lo dice en vez de desaparecer.
        bool several = _zones.Count > 1 && _acciones.PuedeEliminar;
        MenuItem remove = new()
        {
            Header = Loc.Get("Desktops.Remove"),
            Foreground = CloseBackground,
            IsEnabled = several,
            ToolTip = several ? null : Loc.Get(_acciones.PuedeEliminar ? "Desktops.RemoveLast" : "Desktops.RemoveUnsupported")
        };
        ToolTipService.SetShowOnDisabled(remove, true);
        remove.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => RemoveDesktop(zone)));
        menu.Items.Add(remove);

        menu.Closed += (_, _) =>
        {
            _menuOpen = false;

            if (IsOpen && !_dialogOpen && !HasForeground())
            {
                Activate();
            }
        };

        _menuOpen = true;
        menu.IsOpen = true;
    }

    /// <summary>Si la rejilla es ahora mismo la ventana de delante.</summary>
    private bool HasForeground() =>
        _windows.GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(this).Handle;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(Surface);

        // La cruz se mira antes que nada: cae dentro de la celda, asi que si se resolviera
        // despues, pulsarla se entenderia como un clic en la ventana y saltaria a ella.
        GridEntry? closing = CloseButtonAt(_pressedAt);

        if (closing is not null)
        {
            _closedFromCross = true;
            CloseEntry(closing);
            return;
        }

        // Lo mismo con la «×» de un escritorio y con la columna para crear otro: se resuelven al
        // pulsar, y el soltar que viene detrás no cuenta como clic en el fondo.
        int removing = RemoveButtonAt(_pressedAt);

        if (removing >= 0)
        {
            _closedFromCross = true;
            ZoneVisual zone = _zones[removing];
            Dispatcher.BeginInvoke(new Action(() => RemoveDesktop(zone)));
            return;
        }

        if (IsOnNewDesktop(_pressedAt))
        {
            _closedFromCross = true;
            Dispatcher.BeginInvoke(new Action(CreateDesktop));
            return;
        }

        _pressed = HitTest(_pressedAt);
        _dragging = false;

        if (_pressed is not null)
        {
            Surface.CaptureMouse();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(Surface);

        if (_pressed is null)
        {
            Highlight(HitTest(position));
            ShowDesktopControls(position);
            return;
        }

        if (!_dragging)
        {
            Vector travelled = position - _pressedAt;

            if (Math.Abs(travelled.X) < DragThreshold && Math.Abs(travelled.Y) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            _pressed.Frame!.BorderBrush = HoverFrameBrush;
        }

        // The thumbnail itself is what follows the cursor. Nothing is redrawn by this process:
        // the destination rectangle is handed to the compositor and it does the rest.
        LayoutRect dragged = DraggedRect(position, _pressed);
        _pressed.PlaceThumbnail(dragged);

        if (_pressed.Frame is not null)
        {
            Canvas.SetLeft(_pressed.Frame, ToDipX(dragged.Left) - 3);
            Canvas.SetTop(_pressed.Frame, ToDipY(dragged.Top) - 3);
            _pressed.Frame.Width = ToDipX(dragged.Width) + 6;
            _pressed.Frame.Height = ToDipY(dragged.Height) + 6;
        }

        if (_pressed.Label is not null)
        {
            _pressed.Label.Visibility = Visibility.Collapsed;
        }

        ShowDropTarget(ZoneAt(position));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        Surface.ReleaseMouseCapture();

        // El soltar que viene de haber pulsado la cruz no es un clic en el fondo. Sin esto, cerrar
        // una ventana cerraba tambien la rejilla, y para cerrar tres habia que abrirla tres veces.
        if (_closedFromCross)
        {
            _closedFromCross = false;
            return;
        }

        GridEntry? pressed = _pressed;
        bool dragged = _dragging;
        Point position = e.GetPosition(Surface);

        _pressed = null;
        _dragging = false;

        if (pressed is null)
        {
            // Dentro de la zona de un escritorio, aunque sea en un hueco, el clic significa "ir
            // ahi". Es lo que espera cualquiera al pinchar en un escritorio vacio, que si no no
            // tendria forma de llegar a el desde aqui: sin ventanas no hay nada que pulsar.
            int zone = ZoneAt(position);

            if (zone >= 0)
            {
                GoToDesktop(_zones[zone].DesktopId);
                return;
            }

            // Fuera de todas las zonas sigue siendo la otra manera evidente de salir de una vista
            // a pantalla completa.
            HideGrid();
            return;
        }

        if (!dragged)
        {
            GoTo(pressed);
            return;
        }

        ShowDropTarget(-1);

        int target = ZoneAt(position);

        if (target >= 0 && _zones[target].DesktopId != pressed.DesktopId)
        {
            Move(pressed, _zones[target].DesktopId);
            return;
        }

        // Dropped nowhere in particular: put it back where it was.
        Build();
    }

    /// <summary>Where a dragged thumbnail sits: shrunk, and hanging off the cursor.</summary>
    private LayoutRect DraggedRect(Point cursor, GridEntry entry)
    {
        int width = Math.Max(120, entry.Thumbnail.Width / 2);
        int height = Math.Max(80, entry.Thumbnail.Height / 2);

        int x = ToPixelsX(cursor.X) - width / 2;
        int y = ToPixelsY(cursor.Y) - height / 2;

        return new LayoutRect(x, y, width, height);
    }


    /// <summary>
    /// Salta a un escritorio sin activar ninguna ventana concreta.
    /// </summary>
    /// <remarks>
    /// Separado de <see cref="GoTo(GridEntry)"/> porque son dos cosas distintas: aquel lleva a una
    /// ventana y de paso al escritorio en el que esta; este lleva al escritorio y punto, que es lo
    /// unico posible cuando esta vacio.
    ///
    /// Igual que alli, el primer plano anterior se olvida: lo que hubiera delante es justo lo que
    /// el usuario esta dejando atras, y devolverselo al cerrar la vista lo traeria de vuelta desde
    /// el escritorio que acaba de abandonar.
    /// </remarks>
    private void GoToDesktop(Guid desktop)
    {
        _foregroundBefore = 0;

        HideGrid();

        OperationResult switched = _desktops.SwitchToDesktop(desktop);

        _logger.Info(
            LogEvents.DesktopOperation,
            ("Operation", nameof(GoToDesktop)),
            ("Desktop", desktop),
            ("Status", switched.Status));
    }

    private void GoTo(GridEntry entry)
    {
        // Whatever had the foreground before is precisely what the user is leaving behind, so
        // closing must not hand it back on the way to somewhere else.
        _foregroundBefore = 0;

        HideGrid();

        if (entry.DesktopId is Guid desktop)
        {
            _desktops.SwitchToDesktop(desktop);
        }

        OperationResult activated = _windows.ActivateWindow(entry.Window.Handle);

        if (!activated.IsSuccess)
        {
            _logger.Warn(
                LogEvents.DesktopOperationFailed,
                ("Where", nameof(GoTo)),
                ("Window", entry.Window.HandleText),
                ("Error", activated.Error));
        }
    }

    private void Move(GridEntry entry, Guid target)
    {
        OperationResult moved = _desktops.MoveWindowToDesktop(entry.Window.Handle, target);

        if (!moved.IsSuccess)
        {
            _logger.Warn(
                LogEvents.DesktopOperationFailed,
                ("Where", nameof(Move)),
                ("Window", entry.Window.HandleText),
                ("Error", moved.Error));
        }

        // Rebuilt rather than patched: the window that left changes the size of every thumbnail
        // in both zones, so half an update would look worse than a full one.
        Build();
    }

    /// <summary>La ventana cuya cruz de cerrar está bajo ese punto, o null.</summary>
    private GridEntry? CloseButtonAt(Point position)
    {
        int x = ToPixelsX(position.X);
        int y = ToPixelsY(position.Y);

        foreach (GridEntry entry in _entries)
        {
            if (!entry.Close.IsEmpty && entry.Close.Contains(x, y))
            {
                return entry;
            }
        }

        return null;
    }

    private GridEntry? HitTest(Point position)
    {
        int x = ToPixelsX(position.X);
        int y = ToPixelsY(position.Y);

        foreach (GridEntry entry in _entries)
        {
            if (entry.Thumbnail.Contains(x, y) || entry.Caption.Contains(x, y))
            {
                return entry;
            }
        }

        return null;
    }

    private int ZoneAt(Point position)
    {
        int x = ToPixelsX(position.X);
        int y = ToPixelsY(position.Y);

        for (int index = 0; index < _zones.Count; index++)
        {
            if (_zones[index].Bounds.Contains(x, y))
            {
                return index;
            }
        }

        return -1;
    }

    private void Highlight(GridEntry? entry)
    {
        foreach (GridEntry candidate in _entries)
        {
            if (candidate.Frame is null)
            {
                continue;
            }

            candidate.Frame.BorderBrush = ReferenceEquals(candidate, entry) ? HoverFrameBrush : FrameBrush;
        }

        Mouse.OverrideCursor = entry is null ? null : Cursors.Hand;
    }

    private void ShowDropTarget(int zone)
    {
        if (zone == _dropZone)
        {
            return;
        }

        _dropZone = zone;

        for (int index = 0; index < _zones.Count; index++)
        {
            ZoneVisual visual = _zones[index];

            visual.Panel.BorderBrush = index == zone
                ? DropZoneBorder
                : visual.IsCurrent ? CurrentZoneBorder : ZoneBorder;

            visual.Panel.BorderThickness = new Thickness(index == zone ? 3 : visual.IsCurrent ? 2 : 1);
        }
    }

    private int ToPixelsX(double dip) => (int)Math.Round(dip * _dpi.DpiScaleX);

    private int ToPixelsY(double dip) => (int)Math.Round(dip * _dpi.DpiScaleY);

    private double ToDipX(int pixels) => pixels / _dpi.DpiScaleX;

    private double ToDipY(int pixels) => pixels / _dpi.DpiScaleY;

    /// <summary>One window in the overview: what it is, where it is drawn and its live picture.</summary>
    private sealed class GridEntry : IDisposable
    {
        private readonly DwmThumbnail? _thumbnail;

        public GridEntry(WindowInfo window, Guid? desktopId, DwmThumbnail? thumbnail)
        {
            Window = window;
            DesktopId = desktopId;
            _thumbnail = thumbnail;
        }

        public WindowInfo Window { get; }

        /// <summary>Null for a window pinned to every desktop.</summary>
        public Guid? DesktopId { get; }

        /// <summary>
        /// Shape of the real window, so the thumbnail is not stretched into its cell.
        /// </summary>
        /// <remarks>
        /// The compositor is asked first because it knows the size of the surface it will
        /// actually draw. The window rectangle is the fallback for the case where there is no
        /// thumbnail at all, and zero says "no idea", which the layout answers with a sane shape.
        /// </remarks>
        public double AspectRatio
        {
            get
            {
                if (_thumbnail is not null && _thumbnail.AspectRatio > 0)
                {
                    return _thumbnail.AspectRatio;
                }

                return Window.Bounds.Height > 0
                    ? Window.Bounds.Width / (double)Window.Bounds.Height
                    : 0;
            }
        }

        public LayoutRect Thumbnail { get; set; }

        public LayoutRect Caption { get; set; }

        /// <summary>La cruz de cerrar. Vacía cuando la miniatura es demasiado pequeña.</summary>
        public LayoutRect Close { get; set; }

        public Border? Frame { get; set; }

        public TextBlock? Label { get; set; }

        public Border? CloseButton { get; set; }

        public void PlaceThumbnail() => PlaceThumbnail(Thumbnail);

        public void PlaceThumbnail(LayoutRect rect) =>
            _thumbnail?.Place(rect, Window.IsMinimized ? MinimisedOpacity : (byte)255);

        public void Dispose() => _thumbnail?.Dispose();
    }

    /// <summary>One desktop zone, kept so a drag can light up the one under the cursor.</summary>
    private sealed record ZoneVisual(int Index, Guid DesktopId, LayoutRect Bounds, Border Panel, bool IsCurrent);
}
