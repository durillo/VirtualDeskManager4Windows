using System.ComponentModel;

namespace VirtualDeskManager.Core.Localization;

/// <summary>
/// Translated user interface strings.
/// </summary>
/// <remarks>
/// The indexer plus <see cref="INotifyPropertyChanged"/> is what lets WPF re-evaluate every
/// binding when the language changes: raising <c>PropertyChanged("Item[]")</c> refreshes the
/// whole window without recreating it.
/// Log messages are deliberately left in English: they are read by developers, not by users.
/// </remarks>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>Languages the user can pick, in the order the picker shows them.</summary>
    IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    /// <summary>Language in use right now; never "auto", always a real one.</summary>
    LanguageInfo Current { get; }

    /// <summary>Whether the current language needs the interface mirrored.</summary>
    bool IsRightToLeft { get; }

    /// <summary>Translation of <paramref name="key"/>, or the key itself when it is missing.</summary>
    string this[string key] { get; }

    /// <summary>
    /// Translation of <paramref name="key"/> in the current language, falling back to English and
    /// then to the key itself.
    /// </summary>
    string Get(string key);

    /// <summary>
    /// <see cref="Get"/> with <c>{0}</c>-style placeholders filled in. A translation with a broken
    /// placeholder comes back unformatted instead of throwing.
    /// </summary>
    string Format(string key, params object?[] arguments);

    /// <summary>Switches language. <c>null</c> or <c>"auto"</c> follows the Windows language.</summary>
    void Use(string? code);

    /// <summary>Raised after <see cref="Use"/> actually switched to a different language.</summary>
    event EventHandler? LanguageChanged;
}
