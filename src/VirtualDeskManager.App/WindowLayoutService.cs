using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Layout;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.App;

/// <summary>
/// Recuerda dónde está cada ventana y las devuelve a su sitio cuando Windows las amontona.
/// </summary>
/// <remarks>
/// El caso que resuelve: al suspender o bloquear el equipo, las pantallas se apagan; Windows se
/// queda entonces con una sola y mete en ella todas las ventanas. Al despertar, las pantallas
/// vuelven pero las ventanas no.
///
/// La idea entera está en <see cref="DisplayFingerprint"/>: se guarda una distribución por
/// configuración de pantallas, de modo que el amontonamiento se guarda bajo su propia huella —la
/// de "una sola pantalla"— y no puede pisar la buena. Cuando las pantallas vuelven, la huella
/// vuelve a ser la de antes y con ella la distribución correcta.
/// </remarks>
public sealed class WindowLayoutService : IDisposable
{
    /// <summary>
    /// Cuánto se considera que dura el desbarajuste tras un cambio de pantallas.
    /// </summary>
    /// <remarks>
    /// Mientras dura, no se guarda nada. Es la otra mitad de la protección: sin esto, el guardado
    /// periódico podría caer justo en medio del amontonamiento y grabarlo bajo la huella de la
    /// configuración buena, que es exactamente el desastre que se quiere evitar.
    /// </remarks>
    private static readonly TimeSpan SettlingPeriod = TimeSpan.FromSeconds(20);

    private readonly Dispatcher _dispatcher;
    private readonly IWindowManager _windows;
    private readonly WindowLayoutRepository _repository;
    private readonly Func<WindowLayoutSettings> _settings;
    private readonly Win32MessageLoop _loop;
    private readonly uint _ownProcessId;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _restoreTimer;
    private readonly object _gate = new();

    private List<WindowLayoutSnapshot> _snapshots = new();
    private DateTimeOffset _disturbedUntil = DateTimeOffset.MinValue;
    private bool _started;
    private bool _disposed;

    /// <summary>Prepara los temporizadores; no lee nada ni escucha avisos hasta <see cref="Start"/>.</summary>
    /// <param name="dispatcher">Un hilo con bomba de mensajes para los temporizadores: el de la interfaz.</param>
    /// <param name="windows">Para leer y recolocar las ventanas y saber qué pantallas hay.</param>
    /// <param name="repository">Dónde se guardan las distribuciones.</param>
    /// <param name="settings">Los ajustes de la función; se leen cada vez que hacen falta.</param>
    /// <param name="loop">El bucle de mensajes Win32, por donde llegan los cambios de pantalla y el despertar.</param>
    /// <param name="ownProcessId">El proceso de Vdesk, cuyas ventanas no se tocan.</param>
    /// <param name="logger">Dónde apuntar lo que pase.</param>
    public WindowLayoutService(
        Dispatcher dispatcher,
        IWindowManager windows,
        WindowLayoutRepository repository,
        Func<WindowLayoutSettings> settings,
        Win32MessageLoop loop,
        uint ownProcessId,
        IAppLogger? logger = null)
    {
        _dispatcher = dispatcher;
        _windows = windows;
        _repository = repository;
        _settings = settings;
        _loop = loop;
        _ownProcessId = ownProcessId;
        _logger = logger ?? NullAppLogger.Instance;

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _saveTimer.Tick += (_, _) => SaveCurrent();

        // Un único disparo: se rearma en cada aviso, de modo que una ráfaga de cambios de pantalla
        // —que es lo normal al conectar una base— acaba en una sola recolocación al final.
        _restoreTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _restoreTimer.Tick += (_, _) =>
        {
            _restoreTimer.Stop();
            Restore(automatic: true);
        };
    }

    /// <summary>
    /// Carga lo guardado, se pone a escuchar cambios de pantalla y vuelta de suspensión, y arranca
    /// el guardado periódico. Si los ajustes lo piden, programa una recolocación al arrancar.
    /// </summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _snapshots = _repository.Load().ToList();

        _loop.MessageReceived += OnMessage;

        WindowLayoutSettings settings = _settings();
        settings.Normalise();

        _saveTimer.Interval = TimeSpan.FromSeconds(settings.SaveIntervalSeconds);
        _saveTimer.Start();

        _logger.Info(
            "WindowLayoutStarted",
            ("Layouts", _snapshots.Count),
            ("Displays", CurrentFingerprint().Value));

        if (settings.RestoreOnStartup)
        {
            Schedule(settings.RestoreDelayMilliseconds);
        }
    }

    /// <summary>Guarda la distribución de ahora bajo la huella de las pantallas de ahora.</summary>
    public void SaveCurrent()
    {
        WindowLayoutSettings settings = _settings();

        if (!settings.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (DateTimeOffset.UtcNow < _disturbedUntil)
            {
                // Las pantallas acaban de cambiar: lo que hay en la pantalla ahora mismo no
                // describe donde el usuario quiere sus ventanas.
                return;
            }

            DisplayFingerprint fingerprint = CurrentFingerprint();

            if (!fingerprint.IsKnown)
            {
                return;
            }

            List<WindowPlacement> placements = Placements();

            if (placements.Count == 0)
            {
                return;
            }

            _snapshots = WindowLayoutStore
                .Remember(_snapshots, new WindowLayoutSnapshot(fingerprint, DateTimeOffset.Now, placements))
                .ToList();

            _repository.Save(_snapshots);
        }
    }

    /// <summary>
    /// Devuelve las ventanas a donde estaban con esta configuración de pantallas.
    /// </summary>
    /// <returns>Cuántas se han movido, o -1 si no había nada guardado para estas pantallas.</returns>
    public int Restore(bool automatic = false)
    {
        WindowLayoutSettings settings = _settings();

        if (automatic && !settings.RestoreAutomatically)
        {
            return 0;
        }

        DisplayFingerprint fingerprint = CurrentFingerprint();
        WindowLayoutSnapshot? snapshot;

        lock (_gate)
        {
            snapshot = WindowLayoutStore.Find(_snapshots, fingerprint);
        }

        if (snapshot is null)
        {
            _logger.Info("WindowLayoutUnknown", ("Displays", fingerprint.Value));
            return -1;
        }

        IReadOnlyList<WindowInfo> windows = Manageable();
        IReadOnlyList<PlacementRestore> restores = WindowLayoutStore.Match(snapshot, windows);

        int moved = 0;

        foreach (PlacementRestore restore in restores)
        {
            WindowBounds target = restore.Placement.Bounds;

            // Restaurar antes de mover: a una ventana maximizada, MoveWindow no le hace nada y
            // devuelve que ha ido bien, asi que sin esto la mitad no se recolocaria en silencio.
            if (restore.Placement.State != WindowVisualState.Maximized)
            {
                _windows.SetWindowState(restore.Handle, WindowVisualState.Normal);
            }

            OperationResult result = _windows.MoveWindow(
                restore.Handle,
                target.X,
                target.Y,
                target.Width,
                target.Height);

            if (result.IsSuccess && restore.Placement.State == WindowVisualState.Maximized)
            {
                _windows.SetWindowState(restore.Handle, WindowVisualState.Maximized);
            }

            if (result.IsSuccess)
            {
                moved++;
            }
        }

        _logger.Info(
            "WindowLayoutRestored",
            ("Displays", fingerprint.Value),
            ("Saved", snapshot.Placements.Count),
            ("Matched", restores.Count),
            ("Moved", moved),
            ("Automatic", automatic));

        // Lo que hay en pantalla vuelve a ser lo que el usuario quiere, asi que se puede guardar.
        lock (_gate)
        {
            _disturbedUntil = DateTimeOffset.MinValue;
        }

        return moved;
    }

    /// <summary>Cuántas configuraciones de pantallas hay guardadas, para enseñarlo en los ajustes.</summary>
    public int SavedLayoutCount
    {
        get
        {
            lock (_gate)
            {
                return _snapshots.Count;
            }
        }
    }

    /// <summary>Si hay algo guardado para las pantallas de ahora mismo.</summary>
    public bool HasLayoutForCurrentDisplays()
    {
        lock (_gate)
        {
            return WindowLayoutStore.Find(_snapshots, CurrentFingerprint()) is not null;
        }
    }

    /// <summary>Borra todas las distribuciones guardadas, de todas las configuraciones de pantallas.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _snapshots = new List<WindowLayoutSnapshot>();
            _repository.Save(_snapshots);
        }
    }

    private void OnMessage(object? sender, Win32Message message)
    {
        bool interesting = message.Message switch
        {
            NativeConstants.WM_DISPLAYCHANGE => true,
            NativeConstants.WM_POWERBROADCAST =>
                (int)message.WParam is NativeConstants.PBT_APMRESUMESUSPEND
                    or NativeConstants.PBT_APMRESUMEAUTOMATIC,
            _ => false
        };

        if (!interesting)
        {
            return;
        }

        lock (_gate)
        {
            _disturbedUntil = DateTimeOffset.UtcNow + SettlingPeriod;
        }

        _logger.Info("WindowLayoutDisturbed", ("Message", message.Message));

        // Esto llega en el hilo del bucle de mensajes, no en el de la interfaz.
        _dispatcher.BeginInvoke(new Action(() => Schedule(_settings().RestoreDelayMilliseconds)));
    }

    private void Schedule(int delayMilliseconds)
    {
        _restoreTimer.Stop();
        _restoreTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(delayMilliseconds, 500, 15000));
        _restoreTimer.Start();
    }

    private DisplayFingerprint CurrentFingerprint()
    {
        try
        {
            return DisplayFingerprint.Of(_windows.GetMonitors());
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(CurrentFingerprint)));
            return DisplayFingerprint.Unknown;
        }
    }

    private List<WindowPlacement> Placements() => Manageable()
        .Select(window => new WindowPlacement(
            window.ProcessName,
            window.ClassName,
            window.Title,
            window.Bounds,
            WindowLayoutStore.CurrentState(window)))
        .ToList();

    /// <summary>
    /// Las ventanas que tiene sentido recolocar.
    /// </summary>
    /// <remarks>
    /// Fuera las minimizadas, porque sus coordenadas no son las de su sitio sino las del limbo
    /// donde Windows las guarda, y fuera las nuestras, para no perseguirnos a nosotros mismos.
    /// </remarks>
    private IReadOnlyList<WindowInfo> Manageable()
    {
        try
        {
            return _windows.GetTopLevelWindows()
                .Where(window => window.ProcessId != _ownProcessId)
                .Where(window => window.IsVisible && !window.IsMinimized)
                .Where(window => !window.Bounds.IsEmpty)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Manageable)));
            return Array.Empty<WindowInfo>();
        }
    }

    /// <summary>Deja de escuchar, para los temporizadores y guarda una última vez la distribución de ahora.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _loop.MessageReceived -= OnMessage;
        _saveTimer.Stop();
        _restoreTimer.Stop();

        // Una última al cerrar: si el usuario sale de la aplicación y luego reinicia, lo que se
        // reponga será lo de ese momento y no lo de hace medio minuto.
        SaveCurrent();
    }
}
