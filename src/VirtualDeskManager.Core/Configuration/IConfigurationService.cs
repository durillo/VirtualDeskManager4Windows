using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// Reads and writes the three configuration files: settings.json, hotkeys.json and rules.json
/// (spec section 24).
/// </summary>
public interface IConfigurationService
{
    /// <summary>Where the configuration files live.</summary>
    ConfigurationPaths Paths { get; }

    /// <summary>
    /// The settings in memory. Changing them does not write anything; call
    /// <see cref="SaveSettings"/> afterwards.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>The valid hotkeys from the last load or save. Broken entries are left out and reported in <see cref="Issues"/>.</summary>
    IReadOnlyList<HotkeyDefinition> Hotkeys { get; }

    /// <summary>The window rules from the last load or save.</summary>
    IReadOnlyList<WindowRule> Rules { get; }

    /// <summary>Problems found while loading. Shown in the UI, never thrown (spec section 35).</summary>
    IReadOnlyList<ConfigurationIssue> Issues { get; }

    /// <summary>Raised after <see cref="Reload"/>, so that whoever caches settings, hotkeys or rules can pick up the new ones.</summary>
    event EventHandler? Reloaded;

    /// <summary>Loads every file, writing the defaults for the ones that do not exist yet.</summary>
    void Load();

    /// <summary>Loads everything again from disk and raises <see cref="Reloaded"/>.</summary>
    void Reload();

    /// <summary>Writes <see cref="Settings"/> to settings.json. A failure is reported in <see cref="Issues"/>, not thrown.</summary>
    void SaveSettings();

    /// <summary>Replaces the rules in memory and writes them to rules.json.</summary>
    void SaveRules(IEnumerable<WindowRule> rules);

    /// <summary>Replaces the hotkeys in memory and writes them to hotkeys.json.</summary>
    void SaveHotkeys(IEnumerable<HotkeyDefinition> hotkeys);
}
