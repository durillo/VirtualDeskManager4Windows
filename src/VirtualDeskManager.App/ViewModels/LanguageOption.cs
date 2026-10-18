using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>
/// One entry of the language picker. The ten languages show their own native name, which never
/// needs translating; only the "same as Windows" entry follows the current language.
/// </summary>
public sealed class LanguageOption : ObservableObject
{
    private readonly LanguageInfo? _language;

    private LanguageOption(string code, LanguageInfo? language)
    {
        Code = code;
        _language = language;
    }

    /// <summary>The "same as Windows" entry.</summary>
    public static LanguageOption Automatic() => new(LocalizationService.AutomaticCode, null);

    /// <summary>The entry for one of the bundled languages.</summary>
    public static LanguageOption For(LanguageInfo language) => new(language.Code, language);

    /// <summary>
    /// What is saved in the settings: the language code, or
    /// <see cref="LocalizationService.AutomaticCode"/> for the automatic entry.
    /// </summary>
    public string Code { get; }

    /// <summary>True for the "same as Windows" entry.</summary>
    public bool IsAutomatic => _language is null;

    /// <summary>The label in the picker: the native name, or the translated "same as Windows".</summary>
    public string Display => _language?.NativeName ?? Loc.Get("Settings.LanguageAuto");

    /// <summary>Called when the language changes so the automatic entry updates its own label.</summary>
    public void RefreshDisplay()
    {
        if (IsAutomatic)
        {
            Raise(nameof(Display));
        }
    }

    /// <summary>The same as <see cref="Display"/>.</summary>
    public override string ToString() => Display;
}
