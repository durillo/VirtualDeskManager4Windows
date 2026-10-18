using VirtualDeskManager.Core.Configuration;

namespace VirtualDeskManager.Core.Backup;

/// <summary>Cliente de nube al que pertenece una carpeta sincronizada.</summary>
public enum CloudProvider
{
    /// <summary>OneDrive, personal o de empresa.</summary>
    OneDrive,
    /// <summary>Google Drive para escritorio.</summary>
    GoogleDrive
}

/// <summary>Una carpeta que un cliente de nube ya está sincronizando en este equipo.</summary>
/// <remarks>
/// Aquí está la idea entera de esta función: OneDrive y Google Drive <b>son carpetas</b>. Escribir
/// dentro de ellas es escribir un fichero, y de subirlo se encarga el cliente que el usuario ya
/// tiene instalado. No hay API, ni OAuth, ni registro de aplicación, ni tokens que caducan, ni una
/// revisión de Google que tarda semanas. Para 8 KB de configuración, todo eso sería coste sin
/// ninguna ventaja.
/// </remarks>
public sealed record CloudFolder(CloudProvider Provider, string Root)
{
    /// <summary>Subcarpeta propia, para no dejar ficheros sueltos en la raíz del usuario.</summary>
    public string Directory => Path.Combine(Root, ConfigurationPaths.ApplicationFolderName);

    /// <summary>Ruta del único fichero de copia que se deja en esa nube.</summary>
    public string File => Path.Combine(Directory, ConfigurationArchive.CloudFileName);
}

/// <summary>
/// Busca carpetas de nube sincronizadas. Sin registro de Windows y sin P/Invoke: variables de
/// entorno y rutas, que es lo que estos clientes dejan puesto.
/// </summary>
public sealed class CloudFolderLocator
{
    /// <summary>Nombres con los que Google Drive monta su unidad, según el idioma del cliente.</summary>
    private static readonly string[] GoogleDriveNames = { "My Drive", "Mi unidad", "Google Drive" };

    private readonly Func<string, string?> _environment;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<IEnumerable<string>> _driveRoots;

    /// <summary>Crea el buscador. Los parámetros sólo se pasan en las pruebas.</summary>
    /// <param name="environment">Lee una variable de entorno; por defecto, la del proceso.</param>
    /// <param name="directoryExists">Dice si existe una carpeta; por defecto, el disco real.</param>
    /// <param name="driveRoots">Raíces de las unidades listas; por defecto, las del equipo.</param>
    public CloudFolderLocator(
        Func<string, string?>? environment = null,
        Func<string, bool>? directoryExists = null,
        Func<IEnumerable<string>>? driveRoots = null)
    {
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _directoryExists = directoryExists ?? Directory.Exists;
        _driveRoots = driveRoots ?? DefaultDriveRoots;
    }

    /// <summary>
    /// Devuelve las carpetas de OneDrive y Google Drive que existen ahora, sin repetir ninguna.
    /// </summary>
    public IReadOnlyList<CloudFolder> Find()
    {
        List<CloudFolder> found = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        void Add(CloudProvider provider, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !_directoryExists(path) || !seen.Add(path))
            {
                return;
            }

            found.Add(new CloudFolder(provider, path));
        }

        // OneDrive deja la ruta en el entorno. Una cuenta personal y una de empresa pueden
        // convivir, y entonces hay dos carpetas distintas y las dos valen.
        Add(CloudProvider.OneDrive, _environment("OneDrive"));
        Add(CloudProvider.OneDrive, _environment("OneDriveCommercial"));
        Add(CloudProvider.OneDrive, _environment("OneDriveConsumer"));

        string? profile = _environment("USERPROFILE");

        if (profile is not null)
        {
            foreach (string name in GoogleDriveNames)
            {
                Add(CloudProvider.GoogleDrive, Path.Combine(profile, name));
            }
        }

        // Drive para escritorio monta una unidad virtual (normalmente G:) en vez de una carpeta
        // dentro del perfil. Sólo miramos las unidades si el cliente está instalado, para no ir
        // tocando unidades de red que puedan estar desconectadas.
        if (GoogleDriveIsInstalled())
        {
            foreach (string root in _driveRoots())
            {
                foreach (string name in GoogleDriveNames)
                {
                    Add(CloudProvider.GoogleDrive, Path.Combine(root, name));
                }
            }
        }

        return found;
    }

    private bool GoogleDriveIsInstalled()
    {
        string? localAppData = _environment("LOCALAPPDATA");

        return localAppData is not null
               && _directoryExists(Path.Combine(localAppData, "Google", "DriveFS"));
    }

    private static IEnumerable<string> DefaultDriveRoots()
    {
        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (DriveInfo drive in drives)
        {
            bool ready;

            try
            {
                ready = drive.IsReady;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (ready)
            {
                yield return drive.RootDirectory.FullName;
            }
        }
    }
}
