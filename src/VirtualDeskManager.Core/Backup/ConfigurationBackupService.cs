using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.Core.Backup;

/// <summary>Una copia guardada, venga del historial local o de la carpeta de nube.</summary>
public sealed record BackupSnapshot(
    string Path,
    DateTimeOffset Taken,
    long Bytes,
    string Machine,
    string Origin,
    bool IsCloud)
{
    /// <summary>Nombre del fichero sin la carpeta, que es lo que se enseña en la lista.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>Resultado de una operación de copia, ya en el idioma del usuario.</summary>
public sealed record BackupOutcome(bool Ok, string Message, string? Path = null);

/// <summary>
/// Copias de seguridad de la configuración: historial local automático, copia en una carpeta de
/// nube ya sincronizada, y exportar / importar a un fichero.
/// </summary>
/// <remarks>
/// El historial es lo que de verdad rescata a la gente. Perder el disco es raro; equivocarse
/// editando una regla y no tener forma de volver atrás pasa cada semana. Por eso se guarda el
/// estado <b>anterior</b> a cada cambio y no el posterior: lo que uno quiere recuperar es cómo
/// estaba antes de tocar.
/// </remarks>
public sealed class ConfigurationBackupService : IConfigurationHistory
{
    /// <summary>Tiempo mínimo entre dos copias automáticas.</summary>
    /// <remarks>
    /// Marcar cinco casillas seguidas son cinco escrituras, y no tiene ningún valor guardar cinco
    /// estados intermedios de los últimos veinte segundos: el que interesa es el de antes de
    /// empezar a tocar, y ése es justo el que se queda.
    /// </remarks>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(2);

    private const string HistoryFolderName = "backups";

    private readonly ConfigurationPaths _paths;
    private readonly Func<BackupSettings> _settings;
    private readonly IAppLogger _logger;
    private readonly CloudFolderLocator _locator;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _applicationVersion;
    private readonly object _gate = new();

    private IReadOnlyList<CloudFolder>? _cloudFolders;
    private string? _lastHistoryHash;
    private string? _lastCloudHash;
    private DateTimeOffset _lastHistoryAt = DateTimeOffset.MinValue;

    /// <summary>Crea el servicio. El historial va en la subcarpeta <c>backups</c> de la configuración.</summary>
    /// <param name="paths">Dónde están los ficheros de configuración.</param>
    /// <param name="settings">Lee los ajustes de copia actuales en cada uso, para no quedarse con unos viejos.</param>
    /// <param name="applicationVersion">Versión que se apunta en el manifiesto de cada copia.</param>
    /// <param name="logger">Diario; si falta, no se registra nada.</param>
    /// <param name="locator">Buscador de carpetas de nube; se cambia en las pruebas.</param>
    /// <param name="clock">Reloj; se cambia en las pruebas.</param>
    public ConfigurationBackupService(
        ConfigurationPaths paths,
        Func<BackupSettings> settings,
        string applicationVersion,
        IAppLogger? logger = null,
        CloudFolderLocator? locator = null,
        Func<DateTimeOffset>? clock = null)
    {
        _paths = paths;
        _settings = settings;
        _applicationVersion = applicationVersion;
        _logger = logger ?? NullAppLogger.Instance;
        _locator = locator ?? new CloudFolderLocator();
        _clock = clock ?? (() => DateTimeOffset.Now);

        HistoryDirectory = Path.Combine(paths.Root, HistoryFolderName);
    }

    /// <summary>Carpeta del historial local de copias.</summary>
    public string HistoryDirectory { get; }

    /// <summary>
    /// Carpetas de nube encontradas en el equipo. Se busca una vez: instalar OneDrive con la
    /// aplicación abierta es bastante raro, y <see cref="RefreshCloudFolders"/> está para eso.
    /// </summary>
    public IReadOnlyList<CloudFolder> CloudFolders => _cloudFolders ??= _locator.Find();

    /// <summary>Vuelve a buscar las carpetas de nube y devuelve la lista nueva.</summary>
    public IReadOnlyList<CloudFolder> RefreshCloudFolders()
    {
        _cloudFolders = _locator.Find();
        return _cloudFolders;
    }

    /// <summary>La carpeta elegida en los ajustes, si sigue existiendo.</summary>
    public CloudFolder? SelectedCloudFolder()
    {
        string configured = _settings().CloudFolder;

        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return CloudFolders.FirstOrDefault(
            folder => string.Equals(folder.Root, configured, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ enganches automáticos

    /// <summary>Guarda el estado actual antes de que lo pisen. Llamado desde la persistencia.</summary>
    public void BeforeChange()
    {
        BackupSettings settings = _settings();

        if (!settings.AutomaticHistory)
        {
            return;
        }

        lock (_gate)
        {
            DateTimeOffset now = _clock();

            if (now - _lastHistoryAt < MinimumInterval)
            {
                return;
            }

            // Sin ficheros todavía no hay nada anterior que guardar: instalación recién hecha.
            if (!File.Exists(_paths.Settings) && !File.Exists(_paths.Hotkeys) && !File.Exists(_paths.Rules))
            {
                return;
            }

            try
            {
                string hash = ConfigurationArchive.Hash(_paths);

                if (hash == _lastHistoryHash || hash == NewestHistoryHash())
                {
                    _lastHistoryHash = hash;
                    return;
                }

                Take(origin: "automatic", now);
                _lastHistoryHash = hash;
                _lastHistoryAt = now;
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.BackupFailed, ex, ("Stage", "history"));
            }
        }
    }

    /// <summary>Deja la configuración nueva en la carpeta de nube, si hay una elegida.</summary>
    public void AfterChange()
    {
        BackupSettings settings = _settings();

        if (!settings.CopyToCloud)
        {
            return;
        }

        CloudFolder? folder = SelectedCloudFolder();

        if (folder is null)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                string hash = ConfigurationArchive.Hash(_paths);

                if (hash == _lastCloudHash)
                {
                    return;
                }

                WriteCloud(folder, "automatic");
                _lastCloudHash = hash;
            }
            catch (Exception ex)
            {
                // Que la nube no esté disponible no puede impedir guardar la configuración: el
                // fichero local ya está escrito y esto es una copia de más.
                _logger.Error(LogEvents.BackupFailed, ex, ("Stage", "cloud"), ("Folder", folder.Root));
            }
        }
    }

    // ------------------------------------------------------------------ acciones del usuario

    /// <summary>Copia a mano: al historial y, si está configurada, también a la nube.</summary>
    public BackupOutcome BackupNow()
    {
        lock (_gate)
        {
            try
            {
                DateTimeOffset now = _clock();
                string path = Take(origin: "manual", now);
                _lastHistoryHash = ConfigurationArchive.Hash(_paths);
                _lastHistoryAt = now;

                CloudFolder? folder = SelectedCloudFolder();

                if (folder is not null)
                {
                    WriteCloud(folder, "manual");
                    _lastCloudHash = _lastHistoryHash;

                    return new BackupOutcome(true, Loc.Instance.Format("Backup.SavedWithCloud", folder.Root), path);
                }

                return new BackupOutcome(true, Loc.Instance.Format("Backup.Saved", path), path);
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.BackupFailed, ex, ("Stage", "manual"));
                return new BackupOutcome(false, Loc.Instance.Format("Backup.Failed", ex.Message));
            }
        }
    }

    /// <summary>
    /// Guarda la configuración actual en un fichero elegido por el usuario. No pasa por el
    /// historial ni por la nube.
    /// </summary>
    public BackupOutcome ExportTo(string destination)
    {
        lock (_gate)
        {
            try
            {
                ConfigurationArchive.Write(_paths, destination, "export", _clock(), _applicationVersion);
                _logger.Info(LogEvents.BackupSaved, ("Target", destination));
                return new BackupOutcome(true, Loc.Instance.Format("Backup.Exported", destination), destination);
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.BackupFailed, ex, ("Stage", "export"));
                return new BackupOutcome(false, Loc.Instance.Format("Backup.Failed", ex.Message));
            }
        }
    }

    /// <summary>
    /// Sustituye la configuración por la del archivo. Guarda antes una copia de lo que había.
    /// </summary>
    public BackupOutcome RestoreFrom(string archivePath)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(archivePath))
                {
                    return new BackupOutcome(false, Loc.Instance.Format("Backup.NotFound", archivePath));
                }

                // Una restauración también se puede equivocar de fichero, así que lo que había
                // queda en el historial y se puede deshacer restaurando la copia de arriba.
                if (File.Exists(_paths.Settings))
                {
                    Take(origin: "before-restore", _clock());
                }

                int restored = ConfigurationArchive.Restore(archivePath, _paths);

                if (restored == 0)
                {
                    return new BackupOutcome(false, Loc.Instance.Get("Backup.Empty"));
                }

                _lastHistoryHash = null;
                _lastCloudHash = null;
                _logger.Info(LogEvents.BackupRestored, ("Source", archivePath), ("Files", restored));

                return new BackupOutcome(true, Loc.Instance.Format("Backup.Restored", restored), archivePath);
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.BackupFailed, ex, ("Stage", "restore"));
                return new BackupOutcome(false, Loc.Instance.Format("Backup.Failed", ex.Message));
            }
        }
    }

    /// <summary>Historial local más la copia de la nube, de la más reciente a la más antigua.</summary>
    public IReadOnlyList<BackupSnapshot> Snapshots()
    {
        List<BackupSnapshot> snapshots = new();

        foreach (string path in Files(HistoryDirectory))
        {
            snapshots.Add(Describe(path, isCloud: false));
        }

        CloudFolder? folder = SelectedCloudFolder();

        if (folder is not null && File.Exists(folder.File))
        {
            snapshots.Add(Describe(folder.File, isCloud: true));
        }

        return snapshots.OrderByDescending(snapshot => snapshot.Taken).ToList();
    }

    /// <summary>Borra el fichero de una copia, sea del historial o el de la nube.</summary>
    public BackupOutcome Delete(BackupSnapshot snapshot)
    {
        try
        {
            File.Delete(snapshot.Path);
            return new BackupOutcome(true, Loc.Instance.Get("Backup.Deleted"));
        }
        catch (Exception ex)
        {
            return new BackupOutcome(false, Loc.Instance.Format("Backup.Failed", ex.Message));
        }
    }

    // ------------------------------------------------------------------ interior

    private string Take(string origin, DateTimeOffset now)
    {
        Directory.CreateDirectory(HistoryDirectory);

        string name = now.ToString("yyyy-MM-dd_HH-mm-ss") + ConfigurationArchive.Extension;
        string path = Path.Combine(HistoryDirectory, name);

        ConfigurationArchive.Write(_paths, path, origin, now, _applicationVersion);
        _logger.Debug(LogEvents.BackupSaved, ("File", name), ("Origin", origin));

        Prune();
        return path;
    }

    private void WriteCloud(CloudFolder folder, string origin)
    {
        // Un único fichero que se sobrescribe, y no una pila de copias fechadas: OneDrive y Drive
        // ya guardan su propio historial de versiones de cada fichero, así que acumular allí sería
        // llenarle la nube al usuario para duplicar algo que su cliente ya hace.
        Directory.CreateDirectory(folder.Directory);
        ConfigurationArchive.Write(_paths, folder.File, origin, _clock(), _applicationVersion);
        _logger.Info(LogEvents.BackupSaved, ("Target", folder.File), ("Origin", origin));
    }

    private void Prune()
    {
        int limit = Math.Clamp(_settings().HistoryLimit, 1, 200);

        foreach (string path in Files(HistoryDirectory).Skip(limit).ToList())
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Otro proceso lo tiene abierto. Se borrará la próxima vez.
            }
        }
    }

    /// <summary>Ficheros del historial, del más reciente al más antiguo.</summary>
    private static IEnumerable<string> Files(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        // El nombre es la fecha, así que ordenar por nombre ordena por fecha y no hace falta
        // preguntarle al disco por la de cada fichero.
        return Directory
            .EnumerateFiles(directory, "*" + ConfigurationArchive.Extension)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private string? NewestHistoryHash()
    {
        string? newest = Files(HistoryDirectory).FirstOrDefault();

        return newest is null ? null : ConfigurationArchive.ReadManifest(newest)?.Hash;
    }

    private static BackupSnapshot Describe(string path, bool isCloud)
    {
        BackupManifest? manifest = ConfigurationArchive.ReadManifest(path);
        FileInfo info = new(path);

        return new BackupSnapshot(
            path,
            manifest?.Taken ?? info.LastWriteTime,
            info.Exists ? info.Length : 0,
            manifest?.Machine ?? "",
            manifest?.Origin ?? "",
            isCloud);
    }
}
