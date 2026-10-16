namespace VirtualDeskManager.Core.Localization;

/// <summary>
/// Application wide instance of the localization service.
/// </summary>
/// <remarks>
/// XAML markup extensions cannot receive constructor injection, so the single instance is
/// reachable from here. Everything else takes <see cref="ILocalizationService"/> as a dependency,
/// and the tests build their own instances.
/// </remarks>
public static class Loc
{
    /// <summary>The one service the whole application shares.</summary>
    public static LocalizationService Instance { get; } = new();

    /// <summary>Shortcut for <see cref="LocalizationService.Get"/> on <see cref="Instance"/>.</summary>
    public static string Get(string key) => Instance.Get(key);

    /// <summary>Shortcut for <see cref="LocalizationService.Format"/> on <see cref="Instance"/>.</summary>
    public static string Format(string key, params object?[] arguments) => Instance.Format(key, arguments);
}
