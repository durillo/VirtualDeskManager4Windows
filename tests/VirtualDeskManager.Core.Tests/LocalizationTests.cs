using System.Text.RegularExpressions;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class LocalizationTests
{
    private static readonly LocalizationService Service = new();

    public static TheoryData<string> Languages()
    {
        TheoryData<string> data = new();

        foreach (LanguageInfo language in new LocalizationService().AvailableLanguages)
        {
            data.Add(language.Code);
        }

        return data;
    }

    [Fact]
    public void The_ten_most_spoken_languages_are_available()
    {
        string[] codes = Service.AvailableLanguages.Select(l => l.Code).ToArray();

        Assert.Equal(10, codes.Length);
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(new[] { "en", "zh", "hi", "es", "fr", "ar", "bn", "pt", "ru", "ur" }, codes);
    }

    [Fact]
    public void Arabic_and_urdu_are_the_right_to_left_ones()
    {
        string[] rightToLeft = Service.AvailableLanguages
            .Where(l => l.IsRightToLeft)
            .Select(l => l.Code)
            .ToArray();

        Assert.Equal(new[] { "ar", "ur" }, rightToLeft);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_translates_every_key(string code)
    {
        IReadOnlyDictionary<string, string> english = Service.GetCatalog("en");
        IReadOnlyDictionary<string, string> catalog = Service.GetCatalog(code);

        string[] missing = english.Keys.Where(k => !catalog.ContainsKey(k)).ToArray();
        string[] unexpected = catalog.Keys.Where(k => !english.ContainsKey(k)).ToArray();

        Assert.True(missing.Length == 0, $"{code}.json is missing: {string.Join(", ", missing)}");
        Assert.True(unexpected.Length == 0, $"{code}.json has unknown keys: {string.Join(", ", unexpected)}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_translation_is_empty(string code)
    {
        IReadOnlyDictionary<string, string> catalog = Service.GetCatalog(code);

        Assert.NotEmpty(catalog);
        Assert.All(catalog, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Placeholders_survive_translation(string code)
    {
        IReadOnlyDictionary<string, string> english = Service.GetCatalog("en");
        IReadOnlyDictionary<string, string> catalog = Service.GetCatalog(code);

        foreach (KeyValuePair<string, string> pair in english)
        {
            string expected = Placeholders(pair.Value);
            string actual = Placeholders(catalog[pair.Key]);

            Assert.True(
                expected == actual,
                $"{code}.json, key {pair.Key}: expected placeholders {expected}, found {actual}");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_capability_command_and_status_has_a_label(string code)
    {
        IReadOnlyDictionary<string, string> catalog = Service.GetCatalog(code);

        foreach (CommandType command in Enum.GetValues<CommandType>())
        {
            if (command == CommandType.None)
            {
                continue;
            }

            Assert.True(catalog.ContainsKey("Command." + command), $"{code}: Command.{command}");
        }

        foreach (OperationStatus status in Enum.GetValues<OperationStatus>())
        {
            Assert.True(catalog.ContainsKey("Status." + status), $"{code}: Status.{status}");
        }

        foreach ((string key, bool _) in new VirtualDesktopCapabilities().Describe())
        {
            Assert.True(catalog.ContainsKey(key), $"{code}: {key}");
        }
    }

    [Fact]
    public void An_unknown_language_falls_back_to_english()
    {
        LocalizationService service = new();
        service.Use("klingon");

        Assert.Equal("en", service.Current.Code);
        Assert.Equal("Rules", service.Get("Tab.Rules"));
    }

    [Fact]
    public void An_unknown_key_returns_the_key_itself()
    {
        LocalizationService service = new();

        Assert.Equal("Nope.Missing", service.Get("Nope.Missing"));
    }

    [Fact]
    public void Switching_language_changes_the_strings_and_notifies()
    {
        LocalizationService service = new();
        int notifications = 0;
        service.LanguageChanged += (_, _) => notifications++;

        service.Use("es");

        Assert.Equal("es", service.Current.Code);
        Assert.Equal("Reglas", service.Get("Tab.Rules"));
        Assert.False(service.IsRightToLeft);
        Assert.Equal(1, notifications);

        service.Use("ar");

        Assert.True(service.IsRightToLeft);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Selecting_the_same_language_twice_does_not_notify_again()
    {
        LocalizationService service = new();
        int notifications = 0;
        service.Use("ru");
        service.LanguageChanged += (_, _) => notifications++;

        service.Use("ru");

        Assert.Equal(0, notifications);
    }

    [Fact]
    public void Format_replaces_the_placeholder()
    {
        LocalizationService service = new();
        service.Use("es");

        Assert.Equal("Regla «Spotify» activada.", service.Format("Rules.ToastEnabled", "Spotify"));
    }

    [Fact]
    public void A_broken_placeholder_returns_the_template_instead_of_throwing()
    {
        LocalizationService service = new();

        // "Tab.Rules" has no placeholder, so extra arguments must simply be ignored.
        Assert.Equal("Rules", service.Format("Tab.Rules", "unused"));
    }

    private static string Placeholders(string value)
    {
        string[] found = Regex.Matches(value, @"\{\d+\}")
            .Select(m => m.Value)
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        return string.Join(",", found);
    }
}
