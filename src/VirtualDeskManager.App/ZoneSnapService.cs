using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using VirtualDeskManager.App.Views;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Zones;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Native;
using VirtualDeskManager.Windows.Zones;

namespace VirtualDeskManager.App;

/// <summary>
/// Arrastra una ventana con Mayús pulsada, enseña las zonas y la encaja donde la sueltes.
/// </summary>
/// <remarks>
/// Es lo que le da sentido a las zonas: con atajos hay que acordarse de qué número es cada sitio,
/// y arrastrando se ve.
///
/// Mayús se consulta con un temporizador en vez de con un gancho de teclado a bajo nivel. Un
/// gancho ve cada tecla que el usuario pulsa en todo el equipo mientras está puesto, y no hace
/// falta: aquí sólo interesa si una tecla concreta está abajo durante los segundos que dura un
/// arrastre. Preguntarlo veinte veces por segundo cuesta nada y no se entera de nada más.
/// </remarks>
public sealed class ZoneSnapService : IDisposable
{
    /// <summary>Cada cuánto se mira si Mayús sigue pulsada y por dónde va el ratón.</summary>
    /// <remarks>
    /// Cincuenta milisegundos: por debajo el resaltado ya no se percibe más rápido, y por encima
    /// se nota el retraso al pasar de una zona a otra.
    /// </remarks>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly Dispatcher _dispatcher;
    private readonly IWindowManager _windows;
    private readonly Func<ZoneSettings> _settings;
    private readonly WindowDragTracker _tracker;
    private readonly WindowEdgeKeeper _edges;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;

    private ZoneOverlayWindow? _overlay;
    private nint _dragging;
    private MonitorInfo? _monitor;
    private IReadOnlyList<WindowBounds> _zones = Array.Empty<WindowBounds>();
    private int _activeZone = -1;
    private bool _disposed;

    /// <summary>Prepara la vigilancia de arrastres, pero no la enciende: eso lo decide <see cref="Apply"/>.</summary>
    /// <param name="dispatcher">El hilo de la interfaz, donde se pinta la superposición.</param>
    /// <param name="windows">Para saber monitores y colocar la ventana.</param>
    /// <param name="settings">Los ajustes de las zonas; se leen cada vez que hacen falta.</param>
    /// <param name="loop">El bucle de mensajes Win32, por donde llega el principio y el fin de cada arrastre.</param>
    /// <param name="corners">Cuadra las esquinas y el borde de lo encajado y se acuerda de cómo estaban.</param>
    /// <param name="logger">Dónde apuntar lo que pase.</param>
    public ZoneSnapService(
        Dispatcher dispatcher,
        IWindowManager windows,
        Func<ZoneSettings> settings,
        Win32MessageLoop loop,
        WindowEdgeKeeper corners,
        IAppLogger? logger = null)
    {
        _dispatcher = dispatcher;
        _windows = windows;
        _settings = settings;
        _edges = corners;
        _logger = logger ?? NullAppLogger.Instance;

        _tracker = new WindowDragTracker(loop, _logger);
        _tracker.DragStarted += (_, hwnd) => _dispatcher.BeginInvoke(new Action(() => OnDragStarted(hwnd)));
        _tracker.DragEnded += (_, hwnd) => _dispatcher.BeginInvoke(new Action(() => OnDragEnded(hwnd)));

        _timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher) { Interval = PollInterval };
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>Enciende o apaga la vigilancia según los ajustes, sin reiniciar.</summary>
    public void Apply()
    {
        ZoneSettings settings = _settings();

        if (settings.Enabled && settings.SnapWhileDragging)
        {
            _tracker.Start();
        }
        else
        {
            _tracker.Stop();
            Cancel();
        }

        // Apagar las zonas, o el pegado de bordes, le devuelve a cada ventana los suyos en el
        // acto. Dejarlas cuadradas después de apagarlo sería no haberlo apagado.
        if (!settings.Enabled || !settings.SeamlessEdges)
        {
            _edges.RestoreAll();
        }
    }

    private void OnDragStarted(nint hwnd)
    {
        ZoneSettings settings = _settings();

        if (!settings.Enabled || !settings.SnapWhileDragging || hwnd == 0)
        {
            return;
        }

        _dragging = hwnd;
        _activeZone = -1;
        _timer.Start();
    }

    private void OnDragEnded(nint hwnd)
    {
        _timer.Stop();

        nint dragged = _dragging;
        int zone = _activeZone;
        MonitorInfo? monitor = _monitor;
        IReadOnlyList<WindowBounds> zones = _zones;

        Cancel();

        // Se encaja si habia zona resaltada al soltar, y no si Mayus sigue pulsada ahora mismo:
        // al soltar el boton mucha gente levanta las dos manos a la vez, y preguntar por la tecla
        // en este instante haria que la mitad de los intentos no hicieran nada.
        if (dragged == 0 || zone < 0 || monitor is null || zone >= zones.Count)
        {
            return;
        }

        WindowBounds target = zones[zone];

        // Una ventana maximizada no se mueve: hay que restaurarla antes o Windows la deja donde
        // estaba y dice que todo ha ido bien.
        WindowInfo? info = _windows.GetWindowInfo(dragged);

        if (info?.IsMaximized == true)
        {
            _windows.SetWindowState(dragged, WindowVisualState.Normal);
        }

        OperationResult result = _windows.MoveWindowVisible(dragged, target.X, target.Y, target.Width, target.Height);

        if (result.IsSuccess && _settings().SeamlessEdges)
        {
            _edges.MakeFlush(dragged);
        }

        _logger.Info(
            "ZoneSnapped",
            ("Window", dragged),
            ("Process", info?.ProcessName ?? "?"),
            ("Monitor", monitor.Number),
            ("Zone", zone + 1),
            ("Status", result.Status));
    }

    /// <summary>
    /// Mira si Mayús sigue pulsada y bajo qué zona está el ratón.
    /// </summary>
    /// <remarks>
    /// El monitor se saca del ratón y no de la ventana: al arrastrar una ventana grande hacia otra
    /// pantalla, la ventana sigue teniendo la mayor parte de su superficie en la de origen durante
    /// un buen rato, y las zonas aparecerían en la pantalla equivocada justo cuando se está
    /// apuntando a la otra.
    /// </remarks>
    private void Poll()
    {
        if (_dragging == 0)
        {
            _timer.Stop();
            return;
        }

        if (!IsShiftDown())
        {
            if (_activeZone >= 0 || _overlay?.IsVisible == true)
            {
                _activeZone = -1;
                _overlay?.HideZones();
            }

            return;
        }

        if (!User32.GetCursorPos(out POINT cursor))
        {
            return;
        }

        MonitorInfo? monitor = MonitorAt(cursor.X, cursor.Y);

        if (monitor is null)
        {
            return;
        }

        if (_monitor?.Number != monitor.Number)
        {
            _monitor = monitor;
            _zones = _settings().For(monitor.Number).Compute(monitor.WorkingArea);
        }

        if (_zones.Count == 0)
        {
            // Este monitor no esta repartido: no se ensena nada y soltar ahi no hace nada, que es
            // mejor que ensenar una unica zona del tamano de la pantalla y mover la ventana sola.
            _activeZone = -1;
            _overlay?.HideZones();
            return;
        }

        _activeZone = ZoneLayout.ZoneUnder(_zones, cursor.X, cursor.Y);

        Overlay().ShowZones(monitor.WorkingArea, _zones, _activeZone);
    }

    private ZoneOverlayWindow Overlay() => _overlay ??= new ZoneOverlayWindow(_windows);

    private MonitorInfo? MonitorAt(int x, int y) =>
        _windows.GetMonitors().FirstOrDefault(
            monitor => x >= monitor.Bounds.X
                       && x < monitor.Bounds.Right
                       && y >= monitor.Bounds.Y
                       && y < monitor.Bounds.Bottom);

    /// <summary>El bit alto dice si la tecla está abajo ahora; el bajo, si se ha pulsado desde la última consulta.</summary>
    private static bool IsShiftDown() => (User32.GetAsyncKeyState(NativeConstants.VK_SHIFT) & 0x8000) != 0;

    private void Cancel()
    {
        _dragging = 0;
        _activeZone = -1;
        _monitor = null;
        _zones = Array.Empty<WindowBounds>();
        _overlay?.HideZones();
    }

    /// <summary>
    /// Deja de vigilar, devuelve a cada ventana encajada sus esquinas y su borde, y cierra la
    /// superposición.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _timer.Stop();
        _tracker.Dispose();
        _edges.RestoreAll();

        _overlay?.Close();
        _overlay = null;
    }
}
