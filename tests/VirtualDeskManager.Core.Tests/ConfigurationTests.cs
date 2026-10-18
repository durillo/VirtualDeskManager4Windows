using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class ConfigurationTests : IDisposable
{
    private readonly string _directory;
    private readonly ConfigurationPaths _paths;

    public ConfigurationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "vdm-tests-" + Guid.NewGuid().ToString("N"));
        _paths = new ConfigurationPaths(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test run.
        }
    }

    [Fact]
    public void Missing_files_produce_the_defaults_and_are_written_to_disk()
    {
        JsonConfigurationService service = new(_paths);

        service.Load();

        Assert.True(File.Exists(_paths.Settings));
        Assert.True(File.Exists(_paths.Hotkeys));
        Assert.True(File.Exists(_paths.Rules));
        Assert.NotEmpty(service.Hotkeys);
        Assert.NotEmpty(service.Rules);
        Assert.Empty(service.Issues);
    }

    [Fact]
    public void Invalid_json_does_not_throw_and_is_reported()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_paths.Rules, "{ this is not json");

        JsonConfigurationService service = new(_paths);
        service.Load();

        Assert.Empty(service.Rules);
        Assert.Contains(service.Issues, i => i.File == "rules.json");
    }

    [Fact]
    public void A_newer_schema_version_is_reported_but_still_loaded()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_paths.Rules, """{ "schemaVersion": 99, "rules": [] }""");

        JsonConfigurationService service = new(_paths);
        service.Load();

        Assert.Contains(service.Issues, i => i.Message.Contains("99"));
    }

    [Fact]
    public void Hotkeys_are_read_from_the_documented_format()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _paths.Hotkeys,
            """
            {
              "schemaVersion": 1,
              "hotkeys": [
                {
                  "id": "toggle-topmost",
                  "modifiers": [ "Control", "Alt" ],
                  "key": "T",
                  "command": { "type": "ToggleAlwaysOnTop" }
                }
              ]
            }
            """);

        JsonConfigurationService service = new(_paths);
        service.Load();

        HotkeyDefinition hotkey = Assert.Single(service.Hotkeys);
        Assert.Equal("toggle-topmost", hotkey.Id);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hotkey.Modifiers);
        Assert.Equal(HotkeyKey.T, hotkey.Key);
        Assert.Equal(CommandType.ToggleAlwaysOnTop, hotkey.Command.Type);
        Assert.Equal("Ctrl + Alt + T", hotkey.DisplayText);
    }

    [Fact]
    public void An_unknown_key_or_command_is_reported_and_skipped()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _paths.Hotkeys,
            """
            {
              "hotkeys": [
                { "id": "bad-key", "modifiers": [ "Control" ], "key": "NotAKey", "command": { "type": "ToggleAlwaysOnTop" } },
                { "id": "bad-cmd", "modifiers": [ "Control" ], "key": "J", "command": { "type": "DoSomething" } }
              ]
            }
            """);

        JsonConfigurationService service = new(_paths);
        service.Load();

        Assert.Empty(service.Hotkeys);
        Assert.Equal(2, service.Issues.Count);
    }

    /// <summary>
    /// A bare key survives a round trip through the file. It used to be dropped on load, which
    /// meant a shortcut the user had written by hand vanished without a word.
    /// </summary>
    [Fact]
    public void A_hotkey_without_modifiers_is_kept()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _paths.Hotkeys,
            """
            { "hotkeys": [ { "id": "naked", "modifiers": [], "key": "T", "command": { "type": "ToggleAlwaysOnTop" } } ] }
            """);

        JsonConfigurationService service = new(_paths);
        service.Load();

        Assert.Single(service.Hotkeys);
        Assert.Equal(HotkeyKey.T, service.Hotkeys[0].Key);
        Assert.Equal(HotkeyModifiers.None, service.Hotkeys[0].Modifiers);

        // Nor is it reported as a problem with the file: it is a choice, not a mistake.
        Assert.Empty(service.Issues);
    }

    [Fact]
    public void Rules_survive_a_save_and_load_round_trip()
    {
        JsonConfigurationService service = new(_paths);
        service.Load();

        List<WindowRule> rules = service.Rules.ToList();
        rules[0].Enabled = !rules[0].Enabled;
        bool expected = rules[0].Enabled;
        service.SaveRules(rules);

        JsonConfigurationService reloaded = new(_paths);
        reloaded.Load();

        Assert.Equal(expected, reloaded.Rules[0].Enabled);
        Assert.Equal(rules[0].Id, reloaded.Rules[0].Id);
    }

    [Fact]
    public void An_empty_file_falls_back_to_the_defaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_paths.Settings, "   ");

        JsonConfigurationService service = new(_paths);
        service.Load();

        Assert.True(service.Settings.StartMinimized);
        Assert.Contains(service.Issues, i => i.File == "settings.json");
    }
}
