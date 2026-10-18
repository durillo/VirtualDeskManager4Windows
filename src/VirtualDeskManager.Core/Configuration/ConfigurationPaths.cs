namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// Location of the configuration files: <c>%LocalAppData%\VirtualDeskManager\</c> (spec section 24).
/// The Windows registry is only used for the "start with Windows" entry, which is the
/// mechanism Windows itself defines for that.
/// </summary>
public sealed class ConfigurationPaths
{
    /// <summary>Nombre de la carpeta de la aplicación dentro de <c>%LocalAppData%</c>.</summary>
    public const string ApplicationFolderName = "VirtualDeskManager";

    /// <summary>Calcula las rutas; no crea nada en disco.</summary>
    /// <param name="rootDirectory">Carpeta raíz; si es null, la de la aplicación en <c>%LocalAppData%</c>. Las pruebas pasan una temporal.</param>
    public ConfigurationPaths(string? rootDirectory = null)
    {
        Root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName);

        Settings = Path.Combine(Root, "settings.json");
        Hotkeys = Path.Combine(Root, "hotkeys.json");
        Rules = Path.Combine(Root, "rules.json");
        Layouts = Path.Combine(Root, "layouts.json");
        Logs = Path.Combine(Root, "logs");
        Languages = Path.Combine(Root, "lang");
    }

    /// <summary>Carpeta que contiene toda la configuración.</summary>
    public string Root { get; }

    /// <summary>Ruta de settings.json, los ajustes generales.</summary>
    public string Settings { get; }

    /// <summary>Ruta de hotkeys.json, los atajos de teclado.</summary>
    public string Hotkeys { get; }

    /// <summary>Ruta de rules.json, las reglas de ventana.</summary>
    public string Rules { get; }

    /// <summary>
    /// Donde estaba cada ventana, por configuracion de pantallas.
    /// </summary>
    /// <remarks>
    /// Aparte de los tres ficheros de configuracion y <b>fuera de las copias de seguridad</b> a
    /// proposito: unas coordenadas de ventana describen las pantallas de este equipo y no
    /// significan nada en otro. Llevarselas en la copia seria repartir el desorden.
    /// </remarks>
    public string Layouts { get; }

    /// <summary>Carpeta de los ficheros del diario.</summary>
    public string Logs { get; }

    /// <summary>
    /// Optional folder with <c>&lt;code&gt;.json</c> files that override the built in translations.
    /// It is not created automatically: an empty folder would only add noise.
    /// </summary>
    public string Languages { get; }

    /// <summary>Crea la carpeta raíz y la del diario si faltan. La de idiomas no, a propósito.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
    }
}
