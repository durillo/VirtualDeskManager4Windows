namespace VirtualDeskManager.Core.Localization;

/// <summary>
/// A language the application ships with.
/// </summary>
/// <param name="Code">ISO 639-1 code, also the name of the embedded strings file.</param>
/// <param name="NativeName">Name shown in the language picker, written in that same language.</param>
/// <param name="EnglishName">Name used in logs, which stay in English.</param>
/// <param name="IsRightToLeft">Arabic and Urdu need the whole interface mirrored.</param>
public sealed record LanguageInfo(string Code, string NativeName, string EnglishName, bool IsRightToLeft)
{
    /// <summary>The native name, which is what a plain list of languages shows.</summary>
    public override string ToString() => NativeName;
}
