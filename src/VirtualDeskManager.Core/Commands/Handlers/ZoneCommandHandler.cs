using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Zones;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Coloca la ventana activa en una zona del monitor en el que ya está.
/// </summary>
/// <remarks>
/// En el monitor donde está la ventana y no en el principal ni en el del ratón: el atajo dice "a
/// la zona 2", y si eso además se llevara la ventana a otra pantalla, usarlo sin mirar sería
/// arriesgado y nadie lo usaría sin mirar.
///
/// El parámetro <c>zone</c> empieza en uno, como los escritorios y los monitores, porque es lo que
/// el usuario ve escrito en el atajo.
/// </remarks>
public sealed class ZoneCommandHandler : ICommandHandler
{
    private readonly IWindowManager _windows;
    private readonly Func<ZoneSettings> _settings;
    private readonly WindowEdgeKeeper? _edges;
    private readonly IAppLogger _logger;

    /// <summary>Crea el manejador.</summary>
    /// <param name="windows">Localiza y mueve la ventana.</param>
    /// <param name="settings">Da los ajustes de zonas vigentes en cada llamada, para que un cambio valga sin recrear el manejador.</param>
    /// <param name="corners">Cuadra esquinas y borde de la ventana colocada cuando están activados los bordes sin costura; sin él no se tocan.</param>
    /// <param name="logger">Diario; ninguno si es null.</param>
    public ZoneCommandHandler(
        IWindowManager windows,
        Func<ZoneSettings> settings,
        WindowEdgeKeeper? corners = null,
        IAppLogger? logger = null)
    {
        _windows = windows;
        _settings = settings;
        _edges = corners;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[] { CommandType.MoveWindowToZone };

    /// <inheritdoc/>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        ZoneSettings settings = _settings();

        if (!settings.Enabled)
        {
            return Task.FromResult(OperationResult.NotSupported("Las zonas estan desactivadas."));
        }

        nint hwnd = context.TargetWindow != 0 ? context.TargetWindow : _windows.GetForegroundWindow();

        if (hwnd == 0)
        {
            return Task.FromResult(OperationResult.InvalidWindow("No hay ventana en primer plano."));
        }

        WindowInfo? info = _windows.GetWindowInfo(hwnd);

        if (info is null)
        {
            return Task.FromResult(OperationResult.InvalidWindow());
        }

        if (!command.TryGetInt("zone", out int zoneNumber) || zoneNumber < 1)
        {
            return Task.FromResult(OperationResult.Fail("El atajo no dice a que zona."));
        }

        MonitorInfo? monitor = MonitorOf(info);

        if (monitor is null)
        {
            return Task.FromResult(OperationResult.Fail("No se ha podido saber en que monitor esta la ventana."));
        }

        MonitorZoneSettings layout = settings.For(monitor.Number);
        IReadOnlyList<WindowBounds> zones = layout.Compute(monitor.WorkingArea);

        if (zones.Count == 0 || zoneNumber > zones.Count)
        {
            return Task.FromResult(
                OperationResult.Fail($"El monitor {monitor.Number} no tiene zona {zoneNumber}."));
        }

        WindowBounds target = zones[zoneNumber - 1];

        // Una ventana maximizada no se puede mover: hay que restaurarla primero o Windows la deja
        // donde estaba y devuelve que todo ha ido bien.
        if (info.IsMaximized)
        {
            _windows.SetWindowState(hwnd, WindowVisualState.Normal);
        }

        // Visible y no a secas: sin descontar la franja transparente, dos ventanas en zonas
        // contiguas quedan con catorce pixeles de hueco entre ellas.
        OperationResult result = _windows.MoveWindowVisible(hwnd, target.X, target.Y, target.Width, target.Height);

        if (result.IsSuccess && settings.SeamlessEdges)
        {
            _edges?.MakeFlush(hwnd);
        }

        _logger.Log(
            result.IsSuccess ? LogLevel.Information : LogLevel.Warning,
            result.IsSuccess ? LogEvents.CommandDispatched : LogEvents.CommandFailed,
            result.Error,
            null,
            ("Command", CommandType.MoveWindowToZone),
            ("Window", hwnd),
            ("Process", info.ProcessName),
            ("Monitor", monitor.Number),
            ("Zone", zoneNumber),
            ("Bounds", target.ToString()),
            ("Status", result.Status));

        return Task.FromResult(result);
    }

    /// <summary>El monitor que más superficie comparte con la ventana.</summary>
    private MonitorInfo? MonitorOf(WindowInfo window)
    {
        IReadOnlyList<MonitorInfo> monitors = _windows.GetMonitors();

        if (monitors.Count == 0)
        {
            return null;
        }

        MonitorInfo? best = null;
        long bestOverlap = -1;

        foreach (MonitorInfo monitor in monitors)
        {
            long overlap = Overlap(monitor.Bounds, window.Bounds);

            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = monitor;
            }
        }

        // Una ventana que no toca ninguna pantalla —minimizada, o dejada fuera por un cambio de
        // monitores— se coloca en el principal, que es donde el usuario la va a poder ver.
        return bestOverlap > 0 ? best : monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];
    }

    private static long Overlap(WindowBounds a, WindowBounds b)
    {
        long width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        long height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);

        return width <= 0 || height <= 0 ? 0 : width * height;
    }
}
