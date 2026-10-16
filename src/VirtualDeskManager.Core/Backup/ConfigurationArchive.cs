using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using VirtualDeskManager.Core.Configuration;

namespace VirtualDeskManager.Core.Backup;

/// <summary>Lo que se guarda junto a los ficheros para saber qué es una copia al mirarla.</summary>
public sealed class BackupManifest
{
    /// <summary>Versión del formato del manifiesto, por si un día cambia.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Cuándo se hizo la copia.</summary>
    public DateTimeOffset Taken { get; set; }

    /// <summary>Nombre del equipo, para distinguir copias cuando se sincronizan varios.</summary>
    public string Machine { get; set; } = "";

    /// <summary>Versión de VirtualDeskManager que la escribió.</summary>
    public string Application { get; set; } = "";

    /// <summary>Qué la provocó: un cambio automático, una copia a mano, una restauración.</summary>
    public string Origin { get; set; } = "";

    /// <summary>Huella del contenido, para no guardar dos copias idénticas seguidas.</summary>
    public string Hash { get; set; } = "";
}

/// <summary>
/// Empaqueta los tres ficheros de configuración en un único <c>.vdesk</c>, que es un zip normal.
/// </summary>
/// <remarks>
/// Un solo fichero y no tres sueltos porque una configuración es un conjunto: restaurar unas
/// reglas de ayer junto a unos atajos de hoy es justo la mezcla que nadie quiere. Y es un zip
/// corriente a propósito, así que quien sienta curiosidad puede abrirlo y leer el JSON de dentro
/// sin necesitar esta aplicación.
/// </remarks>
public static class ConfigurationArchive
{
    /// <summary>Extensión de los ficheros de copia.</summary>
    public const string Extension = ".vdesk";

    /// <summary>Nombre fijo del fichero que se deja en la nube.</summary>
    public const string CloudFileName = "VirtualDeskManager" + Extension;

    private const string ManifestEntry = "backup.json";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>Los únicos nombres que se empaquetan y los únicos que se restauran.</summary>
    private static readonly string[] Entries = { "settings.json", "hotkeys.json", "rules.json" };

    /// <summary>Nombres de los ficheros de configuración que entran en una copia.</summary>
    public static IReadOnlyList<string> FileNames => Entries;

    private static string SourcePath(ConfigurationPaths paths, string entry) => entry switch
    {
        "settings.json" => paths.Settings,
        "hotkeys.json" => paths.Hotkeys,
        "rules.json" => paths.Rules,
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "Unknown configuration file.")
    };

    /// <summary>
    /// Huella del contenido actual. Cambia cuando cambia cualquiera de los tres ficheros, y es
    /// estable entre ejecuciones: la fecha del fichero no entra.
    /// </summary>
    public static string Hash(ConfigurationPaths paths)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (string entry in Entries)
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(entry));

            string path = SourcePath(paths, entry);

            if (File.Exists(path))
            {
                hash.AppendData(File.ReadAllBytes(path));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// Escribe una copia en <paramref name="destination"/> con el manifiesto y los ficheros que
    /// existan, y devuelve el manifiesto que ha puesto dentro.
    /// </summary>
    /// <param name="paths">Dónde están los ficheros de configuración.</param>
    /// <param name="destination">Ruta completa del <c>.vdesk</c> que se crea o se sustituye.</param>
    /// <param name="origin">Qué provoca la copia; va al manifiesto.</param>
    /// <param name="taken">Fecha que se apunta como la de la copia.</param>
    /// <param name="applicationVersion">Versión de la aplicación que la escribe.</param>
    public static BackupManifest Write(
        ConfigurationPaths paths,
        string destination,
        string origin,
        DateTimeOffset taken,
        string applicationVersion)
    {
        BackupManifest manifest = new()
        {
            Taken = taken,
            Machine = Environment.MachineName,
            Application = applicationVersion,
            Origin = origin,
            Hash = Hash(paths)
        };

        string? directory = Path.GetDirectoryName(destination);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Se escribe al lado y se mueve encima: si algo falla a mitad, la copia anterior sigue
        // siendo una copia válida en vez de un zip truncado.
        string temporary = destination + ".tmp";

        using (FileStream stream = File.Create(temporary))
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
        {
            using (Stream manifestStream = archive.CreateEntry(ManifestEntry).Open())
            {
                JsonSerializer.Serialize(manifestStream, manifest, ManifestOptions);
            }

            foreach (string entry in Entries)
            {
                string source = SourcePath(paths, entry);

                if (File.Exists(source))
                {
                    archive.CreateEntryFromFile(source, entry);
                }
            }
        }

        File.Move(temporary, destination, overwrite: true);
        return manifest;
    }

    /// <summary>
    /// Lee el manifiesto de una copia sin extraer nada. Devuelve <c>null</c> si el fichero no se
    /// puede leer, no es un zip válido o no lleva manifiesto.
    /// </summary>
    public static BackupManifest? ReadManifest(string archivePath)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry? entry = archive.GetEntry(ManifestEntry);

            if (entry is null)
            {
                return null;
            }

            using Stream stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream, ManifestOptions);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deja los ficheros del archivo en la carpeta de configuración, y devuelve cuántos ha puesto.
    /// </summary>
    /// <remarks>
    /// Sólo se extraen los tres nombres conocidos, comparados exactamente. Un zip puede contener
    /// entradas con rutas como <c>..\..\algo</c>, y extraer a ciegas lo que venga dentro de un
    /// fichero descargado de una nube sería escribir donde diga quien lo haya hecho.
    /// </remarks>
    public static int Restore(string archivePath, ConfigurationPaths paths)
    {
        paths.EnsureCreated();
        int restored = 0;

        using ZipArchive archive = ZipFile.OpenRead(archivePath);

        foreach (string entry in Entries)
        {
            ZipArchiveEntry? found = archive.Entries.FirstOrDefault(
                e => string.Equals(e.FullName, entry, StringComparison.OrdinalIgnoreCase));

            if (found is null)
            {
                continue;
            }

            found.ExtractToFile(SourcePath(paths, entry), overwrite: true);
            restored++;
        }

        return restored;
    }
}
