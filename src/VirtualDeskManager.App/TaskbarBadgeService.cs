using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using VirtualDeskManager.App.Overlay;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Taskbar;
using VirtualDeskManager.Windows.Taskbar;

namespace VirtualDeskManager.App;

/// <summary>
/// Marca cada botón de la barra de tareas con el escritorio en el que está esa aplicación.
/// </summary>
/// <remarks>
/// El trabajo caro —recorrer la barra con UI Automation y averiguar a qué aplicación pertenece
/// cada ventana— se hace fuera del hilo de la interfaz, y sólo se vuelve al hilo de la interfaz
/// para pintar. Un barrido en caliente son unas decenas de milisegundos, pero el primero pasa de
/// medio segundo, y hacerlo donde toca dejaría la aplicación congelada justo al encender la
/// función.
///
/// Se repinta por reloj y no por eventos. No es pereza: los botones de la barra se mueven
/// <b>solos</b> —se centran, se reordenan, aparecen y desaparecen con el filtro por escritorio—
/// y de eso no llega ningún aviso al que suscribirse. Un rectángulo viejo no se nota como un
/// hueco, se nota como un número encima de la aplicación equivocada.
/// </remarks>
public sealed class TaskbarBadgeService : IDisposable
{
    /// <summary>Cada cuánto se vuelve a mirar la barra.</summary>
    /// <remarks>
    /// Un segundo y medio: por debajo se nota en el consumo sin que nadie perciba la mejora, y
    /// por encima el distintivo se queda atrás al cambiar de escritorio, que es justo el momento
    /// en que se mira.
    /// </remarks>
    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1.5);

    private readonly Dispatcher _dispatcher;
    private readonly IWindowManager _windows;
    private readonly IVirtualDesktopService _desktops;
    private readonly Func<TaskbarBadgeSettings> _settings;
    private readonly IAppLogger _logger;

    private readonly TaskbarButtonScanner _scanner;
    private readonly AppIdResolver _resolver;

    /// <summary>Una capa por monitor, reutilizada entre repintados.</summary>
    private readonly Dictionary<int, TaskbarBadgeWindow> _layers = new();

    private DispatcherTimer? _timer;

    /// <summary>Evita que dos barridos se pisen cuando uno tarda más de lo normal.</summary>
    private int _working;

    /// <summary>Si ya se dejo constancia en el diario de lo que se encontro la primera vez.</summary>
    private bool _reported;

    /// <summary>Monitores de los que ya se dejo constancia al colocar su capa.</summary>
    private readonly HashSet<int> _painted = new();

    private bool _started;
    private bool _disposed;

    /// <summary>Prepara el escáner de la barra, pero no arranca nada: eso lo decide <see cref="Apply"/>.</summary>
    /// <param name="dispatcher">El hilo de la interfaz, donde se pinta.</param>
    /// <param name="windows">Ventanas y monitores.</param>
    /// <param name="desktops">De dónde sale en qué escritorio está cada ventana.</param>
    /// <param name="settings">Los ajustes de los distintivos; se leen en cada repintado.</param>
    /// <param name="logger">Dónde apuntar lo que pase.</param>
    public TaskbarBadgeService(
        Dispatcher dispatcher,
        IWindowManager windows,
        IVirtualDesktopService desktops,
        Func<TaskbarBadgeSettings> settings,
        IAppLogger? logger = null)
    {
        _dispatcher = dispatcher;
        _windows = windows;
        _desktops = desktops;
        _settings = settings;
        _logger = logger ?? NullAppLogger.Instance;

        _scanner = new TaskbarButtonScanner(_logger);
        _resolver = new AppIdResolver(_logger);
    }

    /// <summary>Enciende o apaga la marca según los ajustes, sin reiniciar.</summary>
    public void Apply()
    {
        TaskbarBadgeSettings settings = _settings();
        settings.Normalise();

        if (settings.Enabled)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;

        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = Refresh };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _logger.Info("TaskbarBadgesStarted");

        Tick();
    }

    private void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _reported = false;
        _painted.Clear();

        _timer?.Stop();
        _timer = null;

        foreach (TaskbarBadgeWindow capa in _layers.Values)
        {
            capa.Clear();
        }

        _logger.Info("TaskbarBadgesStopped");
    }

    private void Tick()
    {
        // Un barrido cada vez. Si el anterior sigue en marcha --el primero lo esta, porque UI
        // Automation tarda en arrancar-- encolar otro solo hace que se adelanten en fila.
        if (Interlocked.CompareExchange(ref _working, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                IReadOnlyList<PlacedBadge> marcas = Compute();

                _dispatcher.BeginInvoke(new Action(() => Paint(marcas)));
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Tick)));
            }
            finally
            {
                Interlocked.Exchange(ref _working, 0);
            }
        });
    }

    /// <summary>Lo caro: leer la barra, las ventanas y los escritorios, y cruzarlo todo.</summary>
    private IReadOnlyList<PlacedBadge> Compute()
    {
        TaskbarBadgeSettings settings = _settings();

        IReadOnlyList<TaskbarButton> botones = _scanner.Scan();

        if (botones.Count == 0)
        {
            return Array.Empty<PlacedBadge>();
        }

        bool primero = !_reported;

        Dictionary<Guid, int> numero = _desktops.GetDesktops()
            .ToDictionary(d => d.Id, d => d.Number);

        int actual = _desktops.GetCurrentDesktop()?.Number ?? 0;

        Dictionary<string, List<int>> porAplicacion = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ancladas = new(StringComparer.OrdinalIgnoreCase);
        HashSet<uint> vivos = new();

        foreach (WindowInfo ventana in _windows.GetTopLevelWindows())
        {
            vivos.Add(ventana.ProcessId);

            if (!ventana.IsVisible)
            {
                continue;
            }

            string? appId = _resolver.Resolve(ventana.Handle, ventana.ProcessId, ventana.ExecutablePath);

            if (string.IsNullOrEmpty(appId))
            {
                continue;
            }

            if (_desktops.IsWindowPinned(ventana.Handle) == true)
            {
                ancladas.Add(appId);
                continue;
            }

            Guid? escritorio = ventana.VirtualDesktopId ?? _desktops.GetWindowDesktopId(ventana.Handle);

            if (escritorio is null || !numero.TryGetValue(escritorio.Value, out int n))
            {
                continue;
            }

            if (!porAplicacion.TryGetValue(appId, out List<int>? lista))
            {
                lista = new List<int>();
                porAplicacion[appId] = lista;
            }

            lista.Add(n);
        }

        // Procesos que ya no existen: sin esto el resolutor va guardando cada aplicacion que se
        // abrio alguna vez desde que arranco Vdesk.
        _resolver.Forget(vivos);

        List<PlacedBadge> marcas = new();

        foreach (TaskbarButton boton in botones)
        {
            IReadOnlyList<int> donde = porAplicacion.TryGetValue(boton.AppId, out List<int>? lista)
                ? lista
                : Array.Empty<int>();

            TaskbarBadge badge = TaskbarBadgePolicy.For(
                donde,
                ancladas.Contains(boton.AppId),
                actual,
                settings);

            if (badge.ShouldShow)
            {
                marcas.Add(new PlacedBadge(badge.Text, TaskbarBadgeLayout.Place(boton.Bounds, settings)));
            }
        }

        // Una vez y no en cada barrido: sirve para saber, al mirar el diario, si la funcion
        // encontro la barra y cuanto de ella acabo marcando. Repetirlo cada segundo y medio
        // llenaria el fichero sin decir nada nuevo.
        if (primero)
        {
            _reported = true;

            _logger.Info(
                "TaskbarBadgesFirstScan",
                ("Buttons", botones.Count),
                ("Badges", marcas.Count),
                ("Desktop", actual));
        }

        return marcas;
    }

    /// <summary>Lo barato, y lo único que toca la interfaz.</summary>
    private void Paint(IReadOnlyList<PlacedBadge> badges)
    {
        if (!_started)
        {
            return;
        }

        TaskbarBadgeSettings settings = _settings();
        IReadOnlyList<MonitorInfo> monitores = _windows.GetMonitors();

        Dictionary<int, List<PlacedBadge>> porMonitor = new();

        foreach (PlacedBadge badge in badges)
        {
            int monitor = MonitorOf(badge.Bounds, monitores);

            if (!porMonitor.TryGetValue(monitor, out List<PlacedBadge>? lista))
            {
                lista = new List<PlacedBadge>();
                porMonitor[monitor] = lista;
            }

            lista.Add(badge);
        }

        // Los monitores que se han quedado sin nada que marcar: si no se limpian, el ultimo
        // distintivo que hubo en esa pantalla se queda ahi para siempre.
        foreach (int monitor in _layers.Keys.Where(m => !porMonitor.ContainsKey(m)).ToList())
        {
            _layers[monitor].Clear();
        }

        foreach ((int monitor, List<PlacedBadge> lista) in porMonitor)
        {
            if (!_layers.TryGetValue(monitor, out TaskbarBadgeWindow? capa))
            {
                capa = new TaskbarBadgeWindow(_windows);
                _layers[monitor] = capa;
            }

            WindowBounds area = Union(lista);

            capa.Draw(area, lista, settings);

            // Una vez por monitor: es lo que hace falta para saber, con dos barras de tareas, si
            // la que no ensena nada es porque no le tocaba o porque su capa no llego a colocarse.
            if (_painted.Add(monitor))
            {
                _logger.Info(
                    "TaskbarBadgeLayerPlaced",
                    ("Monitor", monitor),
                    ("Badges", lista.Count),
                    ("Area", $"{area.X},{area.Y} {area.Width}x{area.Height}"),
                    ("Visible", capa.IsVisible));
            }
        }
    }

    /// <summary>El rectángulo que abarca todos los distintivos de un monitor.</summary>
    private static WindowBounds Union(IReadOnlyList<PlacedBadge> badges)
    {
        int x = badges.Min(b => b.Bounds.X);
        int y = badges.Min(b => b.Bounds.Y);
        int right = badges.Max(b => b.Bounds.Right);
        int bottom = badges.Max(b => b.Bounds.Bottom);

        return new WindowBounds(x, y, right - x, bottom - y);
    }

    private static int MonitorOf(WindowBounds bounds, IReadOnlyList<MonitorInfo> monitors)
    {
        int cx = bounds.X + (bounds.Width / 2);
        int cy = bounds.Y + (bounds.Height / 2);

        foreach (MonitorInfo monitor in monitors)
        {
            if (cx >= monitor.Bounds.X && cx < monitor.Bounds.Right
                && cy >= monitor.Bounds.Y && cy < monitor.Bounds.Bottom)
            {
                return monitor.Number;
            }
        }

        return 0;
    }

    /// <summary>Para el reloj y cierra las capas de todos los monitores.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Stop();

        foreach (TaskbarBadgeWindow capa in _layers.Values)
        {
            capa.Close();
        }

        _layers.Clear();
    }
}
