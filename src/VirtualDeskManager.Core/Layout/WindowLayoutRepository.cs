using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Layout;

/// <summary>Forma de layouts.json.</summary>
public sealed class WindowLayoutFile
{
    /// <summary>Versión del formato del fichero.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Una distribución por configuración de pantallas.</summary>
    public List<SnapshotFile> Layouts { get; set; } = new();
}

/// <summary>Forma en disco de un <see cref="WindowLayoutSnapshot"/>.</summary>
public sealed class SnapshotFile
{
    /// <summary>Texto de la <see cref="DisplayFingerprint"/> con la que se tomó.</summary>
    public string Displays { get; set; } = "";

    /// <summary>Cuándo se tomó.</summary>
    public DateTimeOffset Taken { get; set; }

    /// <summary>Las posiciones de las ventanas.</summary>
    public List<PlacementFile> Windows { get; set; } = new();
}

/// <summary>Forma en disco de un <see cref="WindowPlacement"/>, con el rectángulo en campos sueltos.</summary>
public sealed class PlacementFile
{
    /// <summary>Nombre del proceso dueño de la ventana.</summary>
    public string Process { get; set; } = "";

    /// <summary>Clase de ventana de Win32.</summary>
    public string Class { get; set; } = "";

    /// <summary>Título que tenía la ventana al guardarla.</summary>
    public string Title { get; set; } = "";

    /// <summary>Borde izquierdo, en píxeles de pantalla.</summary>
    public int X { get; set; }

    /// <summary>Borde superior, en píxeles de pantalla.</summary>
    public int Y { get; set; }

    /// <summary>Ancho en píxeles.</summary>
    public int Width { get; set; }

    /// <summary>Alto en píxeles.</summary>
    public int Height { get; set; }

    /// <summary>Si estaba normal, maximizada o minimizada.</summary>
    public WindowVisualState State { get; set; }
}

/// <summary>
/// Lee y escribe las distribuciones de ventanas.
/// </summary>
/// <remarks>
/// Fichero propio y no dentro de settings.json porque son cosas de naturaleza distinta: los
/// ajustes los escribe el usuario y se leen a mano de vez en cuando, y esto lo escribe el programa
/// cada medio minuto y son cientos de líneas de coordenadas. Mezclarlos convertiría settings.json
/// en algo que ya no se puede abrir para mirar qué hay puesto.
/// </remarks>
public sealed class WindowLayoutRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConfigurationPaths _paths;
    private readonly IAppLogger _logger;

    /// <summary>Crea el repositorio sobre <c>layouts.json</c> en la carpeta de configuración.</summary>
    public WindowLayoutRepository(ConfigurationPaths paths, IAppLogger? logger = null)
    {
        _paths = paths;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>
    /// Lee las distribuciones guardadas. Si el fichero no existe o está roto devuelve una lista
    /// vacía y lo apunta en el diario, sin lanzar.
    /// </summary>
    public IReadOnlyList<WindowLayoutSnapshot> Load()
    {
        try
        {
            if (!File.Exists(_paths.Layouts))
            {
                return Array.Empty<WindowLayoutSnapshot>();
            }

            WindowLayoutFile? file = JsonSerializer.Deserialize<WindowLayoutFile>(
                File.ReadAllText(_paths.Layouts),
                Options);

            if (file is null)
            {
                return Array.Empty<WindowLayoutSnapshot>();
            }

            return file.Layouts.Select(ToSnapshot).ToList();
        }
        catch (Exception ex)
        {
            // Un layouts.json roto no puede impedir arrancar: como mucho se pierden unas
            // posiciones, que es exactamente lo que pasaba antes de que existiera esto.
            _logger.Warn(LogEvents.ConfigurationInvalid, ex.Message, ("File", "layouts.json"));
            return Array.Empty<WindowLayoutSnapshot>();
        }
    }

    /// <summary>
    /// Sustituye el fichero entero por estas distribuciones. Escribe al lado y mueve encima, para
    /// no dejar un fichero a medias; los fallos se apuntan en el diario y no se lanzan.
    /// </summary>
    public void Save(IEnumerable<WindowLayoutSnapshot> snapshots)
    {
        try
        {
            _paths.EnsureCreated();

            WindowLayoutFile file = new()
            {
                Layouts = snapshots.Select(ToFile).ToList()
            };

            string temporary = _paths.Layouts + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(file, Options));
            File.Move(temporary, _paths.Layouts, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Warn(LogEvents.ConfigurationInvalid, ex.Message, ("File", "layouts.json"));
        }
    }

    private static WindowLayoutSnapshot ToSnapshot(SnapshotFile file) => new(
        new DisplayFingerprint(file.Displays),
        file.Taken,
        file.Windows
            .Select(window => new WindowPlacement(
                window.Process,
                window.Class,
                window.Title,
                new WindowBounds(window.X, window.Y, window.Width, window.Height),
                window.State))
            .ToList());

    private static SnapshotFile ToFile(WindowLayoutSnapshot snapshot) => new()
    {
        Displays = snapshot.Fingerprint.Value,
        Taken = snapshot.Taken,
        Windows = snapshot.Placements
            .Select(placement => new PlacementFile
            {
                Process = placement.Process,
                Class = placement.ClassName,
                Title = placement.Title,
                X = placement.Bounds.X,
                Y = placement.Bounds.Y,
                Width = placement.Bounds.Width,
                Height = placement.Bounds.Height,
                State = placement.State
            })
            .ToList()
    };
}
