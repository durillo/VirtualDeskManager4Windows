using System.Text.Json;
using System.Text.Json.Serialization;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// JSON persistence (spec section 24). A broken file never prevents the application from
/// starting: the defaults are used and the problem is reported through <see cref="Issues"/>.
/// </summary>
public sealed class JsonConfigurationService : IConfigurationService
{
    /// <summary>Latest schema understood by this build.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Órdenes que existieron en versiones anteriores y se fueron con el enlace entre equipos.</summary>
    private static readonly HashSet<string> RetiredCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "SendClipboardToLinkedMachines",
        "ShowRemoteWindows",
        "ShowFiles"
    };

    private readonly IAppLogger _logger;
    private readonly List<ConfigurationIssue> _issues = new();

    /// <summary>
    /// Crea el servicio sin leer nada todavía: hasta llamar a <see cref="Load"/> sólo tiene los
    /// ajustes por defecto y ningún atajo ni regla.
    /// </summary>
    /// <param name="paths">Dónde están los ficheros; si es null, la carpeta de siempre en <c>%LocalAppData%</c>.</param>
    /// <param name="logger">Diario; ninguno si es null.</param>
    public JsonConfigurationService(ConfigurationPaths? paths = null, IAppLogger? logger = null)
    {
        Paths = paths ?? new ConfigurationPaths();
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <inheritdoc/>
    public ConfigurationPaths Paths { get; }

    /// <summary>Copias de seguridad, cuando la aplicación las engancha. Opcional a propósito.</summary>
    /// <remarks>
    /// Nulo mientras no se engancha nada, y entonces guardar funciona exactamente como antes. Es
    /// lo que permite que las pruebas y los arranques tempranos no arrastren un sistema de copias.
    /// </remarks>
    public IConfigurationHistory? History { get; set; }

    /// <inheritdoc/>
    public AppSettings Settings { get; private set; } = DefaultConfiguration.CreateSettings();

    /// <inheritdoc/>
    public IReadOnlyList<HotkeyDefinition> Hotkeys { get; private set; } = Array.Empty<HotkeyDefinition>();

    /// <inheritdoc/>
    public IReadOnlyList<WindowRule> Rules { get; private set; } = Array.Empty<WindowRule>();

    /// <inheritdoc/>
    /// <remarks>Se vacía al empezar cada carga; lo que falle al guardar se va sumando hasta la siguiente.</remarks>
    public IReadOnlyList<ConfigurationIssue> Issues => _issues;

    /// <inheritdoc/>
    public event EventHandler? Reloaded;

    /// <inheritdoc/>
    /// <remarks>
    /// Un fichero ilegible o vacío no impide arrancar: se usan los valores por defecto para él (en
    /// reglas, ninguna) y se anota en <see cref="Issues"/>. Sólo se escriben los que no existen.
    /// </remarks>
    public void Load()
    {
        _issues.Clear();

        try
        {
            Paths.EnsureCreated();
        }
        catch (Exception ex)
        {
            AddIssue(Paths.Root, $"The configuration folder could not be created: {ex.Message}");
        }

        Settings = LoadSettings();
        Hotkeys = LoadHotkeys();
        Rules = LoadRules();

        _logger.Info(
            LogEvents.ConfigurationLoaded,
            ("Directory", Paths.Root),
            ("Hotkeys", Hotkeys.Count),
            ("Rules", Rules.Count),
            ("Issues", _issues.Count));
    }

    /// <inheritdoc/>
    public void Reload()
    {
        Load();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings LoadSettings()
    {
        AppSettings? loaded = ReadFile<AppSettings>(Paths.Settings, out bool missing);

        if (missing)
        {
            AppSettings defaults = DefaultConfiguration.CreateSettings();
            WriteFile(Paths.Settings, defaults);
            return defaults;
        }

        if (loaded is null)
        {
            return DefaultConfiguration.CreateSettings();
        }

        // Lo que trajera un settings.json de antes y ya no exista aquí (la sección «link» del
        // enlace entre equipos, que ahora es MiRed) se ignora al leer y desaparece al guardar.

        if (loaded.SchemaVersion > CurrentSchemaVersion)
        {
            AddIssue(
                Paths.Settings,
                $"settings.json declares schema {loaded.SchemaVersion} but this build understands {CurrentSchemaVersion}. Unknown values are ignored.");
        }

        loaded.Logging ??= new LoggingSettings();
        return loaded;
    }

    private IReadOnlyList<HotkeyDefinition> LoadHotkeys()
    {
        HotkeysFile? file = ReadFile<HotkeysFile>(Paths.Hotkeys, out bool missing);

        if (missing)
        {
            List<HotkeyDefinition> defaults = DefaultConfiguration.CreateHotkeys();
            WriteHotkeys(defaults);
            return defaults;
        }

        if (file is null)
        {
            return DefaultConfiguration.CreateHotkeys();
        }

        if (file.SchemaVersion > CurrentSchemaVersion)
        {
            AddIssue(Paths.Hotkeys, $"hotkeys.json declares an unknown schema version ({file.SchemaVersion}).");
        }

        List<HotkeyDefinition> result = new();
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (HotkeyEntryFile entry in file.Hotkeys)
        {
            // Los atajos del enlace entre equipos (ahora MiRed) se quitan sin dar la queja.
            if (RetiredCommands.Contains(entry.Command.Type))
            {
                continue;
            }

            HotkeyDefinition? definition = ConfigurationMapper.ToDefinition(entry, out string? error);

            if (definition is null)
            {
                AddIssue(Paths.Hotkeys, error ?? "Invalid hotkey entry.");
                continue;
            }

            if (!seenIds.Add(definition.Id))
            {
                AddIssue(Paths.Hotkeys, $"Duplicated hotkey id '{definition.Id}'; only the first one is used.");
                continue;
            }

            result.Add(definition);
        }

        return result;
    }

    private IReadOnlyList<WindowRule> LoadRules()
    {
        RulesFile? file = ReadFile<RulesFile>(Paths.Rules, out bool missing);

        if (missing)
        {
            List<WindowRule> defaults = DefaultConfiguration.CreateRules();
            WriteRules(defaults);
            return defaults;
        }

        if (file is null)
        {
            return Array.Empty<WindowRule>();
        }

        if (file.SchemaVersion > CurrentSchemaVersion)
        {
            AddIssue(Paths.Rules, $"rules.json declares an unknown schema version ({file.SchemaVersion}).");
        }

        List<WindowRule> result = new();

        foreach (WindowRule rule in file.Rules)
        {
            if (rule.Id == Guid.Empty)
            {
                rule.Id = Guid.NewGuid();
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                rule.Name = rule.Match.Describe();
            }

            if (!rule.Match.HasAnyCriteria)
            {
                AddIssue(Paths.Rules, $"Rule '{rule.Name}' has no criteria and will never match.");
            }

            if (!rule.Actions.HasAny)
            {
                AddIssue(Paths.Rules, $"Rule '{rule.Name}' has no actions.");
            }

            result.Add(rule);
        }

        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Escribe primero a un temporal y luego lo cambia por el bueno, para que un cierre a destiempo
    /// no deje el fichero a medias. Lo mismo vale para reglas y atajos.
    /// </remarks>
    public void SaveSettings() => WriteFile(Paths.Settings, Settings);

    /// <inheritdoc/>
    public void SaveRules(IEnumerable<WindowRule> rules)
    {
        List<WindowRule> snapshot = rules.ToList();
        Rules = snapshot;
        WriteRules(snapshot);
    }

    /// <inheritdoc/>
    public void SaveHotkeys(IEnumerable<HotkeyDefinition> hotkeys)
    {
        List<HotkeyDefinition> snapshot = hotkeys.ToList();
        Hotkeys = snapshot;
        WriteHotkeys(snapshot);
    }

    private void WriteRules(IEnumerable<WindowRule> rules) =>
        WriteFile(Paths.Rules, new RulesFile { SchemaVersion = CurrentSchemaVersion, Rules = rules.ToList() });

    private void WriteHotkeys(IEnumerable<HotkeyDefinition> hotkeys) =>
        WriteFile(
            Paths.Hotkeys,
            new HotkeysFile
            {
                SchemaVersion = CurrentSchemaVersion,
                Hotkeys = hotkeys.Select(ConfigurationMapper.ToFile).ToList()
            });

    private T? ReadFile<T>(string path, out bool missing) where T : class
    {
        missing = false;

        try
        {
            if (!File.Exists(path))
            {
                missing = true;
                return null;
            }

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                AddIssue(path, "The file is empty; the defaults are used.");
                return null;
            }

            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            AddIssue(path, $"Invalid JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            AddIssue(path, $"The file could not be read: {ex.Message}");
            return null;
        }
    }

    private void WriteFile<T>(string path, T value)
    {
        try
        {
            Paths.EnsureCreated();
            History?.BeforeChange();
            string json = JsonSerializer.Serialize(value, SerializerOptions);

            // Write to a temporary file first so a crash cannot leave a truncated configuration.
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);

            _logger.Debug(LogEvents.ConfigurationSaved, ("File", Path.GetFileName(path)));
            History?.AfterChange();
        }
        catch (Exception ex)
        {
            AddIssue(path, $"The file could not be written: {ex.Message}");
        }
    }

    private void AddIssue(string path, string message)
    {
        _issues.Add(new ConfigurationIssue(Path.GetFileName(path), message));
        _logger.Warn(LogEvents.ConfigurationInvalid, message, ("File", Path.GetFileName(path)));
    }
}
