using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace VirtualDeskManager.Core.Localization;

/// <summary>
/// Loads the translations from JSON files embedded in this assembly, with an optional override
/// folder so a translation can be fixed without recompiling (same spirit as spec section 24).
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    /// <summary>Setting value meaning "follow the Windows display language".</summary>
    public const string AutomaticCode = "auto";
    /// <summary>Language used when the requested one does not exist, and for missing strings.</summary>
    public const string FallbackCode = "en";

    private const string ResourcePrefix = "VirtualDeskManager.Core.Localization.Strings.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string> _strings;
    private IReadOnlyDictionary<string, string> _fallback;

    /// <summary>Creates the service in English; call <see cref="Use"/> to switch.</summary>
    /// <param name="overrideDirectory">See <see cref="OverrideDirectory"/>.</param>
    public LocalizationService(string? overrideDirectory = null)
    {
        OverrideDirectory = overrideDirectory;
        _fallback = LoadCatalog(FallbackCode);
        _strings = _fallback;
        Current = Find(FallbackCode)!;
    }

    /// <summary>The ten most spoken languages in the world, by total number of speakers.</summary>
    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; } = new[]
    {
        new LanguageInfo("en", "English", "English", false),
        new LanguageInfo("zh", "中文（简体）", "Chinese (Simplified)", false),
        new LanguageInfo("hi", "हिन्दी", "Hindi", false),
        new LanguageInfo("es", "Español", "Spanish", false),
        new LanguageInfo("fr", "Français", "French", false),
        new LanguageInfo("ar", "العربية", "Arabic", true),
        new LanguageInfo("bn", "বাংলা", "Bengali", false),
        new LanguageInfo("pt", "Português", "Portuguese", false),
        new LanguageInfo("ru", "Русский", "Russian", false),
        new LanguageInfo("ur", "اردو", "Urdu", true)
    };

    /// <summary>Folder scanned for <c>&lt;code&gt;.json</c> files that override the built in strings.</summary>
    public string? OverrideDirectory { get; set; }

    /// <inheritdoc/>
    public LanguageInfo Current { get; private set; }

    /// <inheritdoc/>
    public bool IsRightToLeft => Current.IsRightToLeft;

    /// <inheritdoc/>
    public string this[string key] => Get(key);

    /// <summary>
    /// Raised for <c>Item[]</c>, <see cref="Current"/> and <see cref="IsRightToLeft"/> when the
    /// language changes, so WPF bindings refresh.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public event EventHandler? LanguageChanged;

    /// <inheritdoc/>
    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        IReadOnlyDictionary<string, string> strings;
        IReadOnlyDictionary<string, string> fallback;

        lock (_gate)
        {
            strings = _strings;
            fallback = _fallback;
        }

        if (strings.TryGetValue(key, out string? value) && value.Length > 0)
        {
            return value;
        }

        // An untranslated string falls back to English before showing the raw key.
        return fallback.TryGetValue(key, out string? english) ? english : key;
    }

    /// <inheritdoc/>
    public string Format(string key, params object?[] arguments)
    {
        string template = Get(key);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException)
        {
            // A translation with a broken placeholder must not crash the window.
            return template;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Does nothing, and raises nothing, when the resolved language is already the current one.</remarks>
    public void Use(string? code)
    {
        string resolved = Resolve(code);

        if (string.Equals(resolved, Current.Code, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IReadOnlyDictionary<string, string> catalog = LoadCatalog(resolved);

        lock (_gate)
        {
            _strings = catalog;
        }

        Current = Find(resolved) ?? Find(FallbackCode)!;

        // "Item[]" is the WPF convention for "every indexer value may have changed".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRightToLeft)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Turns "auto", null or an unknown code into a language that actually exists.</summary>
    public string Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, AutomaticCode, StringComparison.OrdinalIgnoreCase))
        {
            code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }

        return Find(code) is { } language ? language.Code : FallbackCode;
    }

    /// <summary>The shipped language with that code, ignoring case, or <c>null</c>.</summary>
    public LanguageInfo? Find(string? code) =>
        code is null
            ? null
            : AvailableLanguages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>All the keys of a language. Used by the tests to check that nothing is missing.</summary>
    public IReadOnlyDictionary<string, string> GetCatalog(string code) => LoadCatalog(code);

    private IReadOnlyDictionary<string, string> LoadCatalog(string code)
    {
        lock (_gate)
        {
            if (_catalogs.TryGetValue(code, out IReadOnlyDictionary<string, string>? cached))
            {
                return cached;
            }
        }

        Dictionary<string, string> catalog = ReadEmbedded(code);

        foreach (KeyValuePair<string, string> pair in ReadOverride(code))
        {
            catalog[pair.Key] = pair.Value;
        }

        lock (_gate)
        {
            _catalogs[code] = catalog;
        }

        return catalog;
    }

    private static Dictionary<string, string> ReadEmbedded(string code)
    {
        try
        {
            Assembly assembly = typeof(LocalizationService).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(ResourcePrefix + code + ".json");

            if (stream is null)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, SerializerOptions)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception)
        {
            // A corrupt resource degrades to English instead of taking the application down.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private Dictionary<string, string> ReadOverride(string code)
    {
        string? directory = OverrideDirectory;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            string path = Path.Combine(directory, code + ".json");

            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), SerializerOptions)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
