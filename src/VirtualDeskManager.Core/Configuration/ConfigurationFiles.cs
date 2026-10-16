using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// Serialization shape of hotkeys.json (spec section 26). Modifiers are stored as a string list
/// because <c>["Control", "Alt"]</c> reads better in a hand edited file than a flags number.
/// </summary>
public sealed class HotkeyEntryFile
{
    /// <summary>Identificador del atajo. Sin él la entrada se descarta al leer.</summary>
    public string Id { get; set; } = "";

    /// <summary>Modificadores por nombre: <c>Control</c> (o <c>Ctrl</c>), <c>Alt</c>, <c>Shift</c>, <c>Windows</c> (o <c>Win</c>).</summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>Nombre de la tecla, uno de los de <see cref="HotkeyKey"/>, sin distinguir mayúsculas.</summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// La segunda combinación, opcional.
    /// </summary>
    /// <remarks>
    /// Anulables y con nombre aparte en vez de convertir el fichero en una lista de
    /// combinaciones: así los hotkeys.json que ya existen siguen leyéndose sin tocarlos, y un
    /// atajo sin segunda combinación se guarda exactamente igual que antes, sin campos vacíos
    /// ensuciando el fichero.
    /// </remarks>
    public List<string>? AlternateModifiers { get; set; }

    /// <summary>Tecla de la segunda combinación. Null o vacía: no hay segunda, digan lo que digan sus modificadores.</summary>
    public string? AlternateKey { get; set; }

    /// <summary>Si el atajo se registra. Activado de serie.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Si mantener pulsadas las teclas repite la orden. Desactivado de serie.</summary>
    public bool AllowRepeat { get; set; }

    /// <summary>La orden que lanza el atajo.</summary>
    public CommandFile Command { get; set; } = new();
}

/// <summary>Forma en el fichero de una orden: su tipo por nombre y sus parámetros como texto.</summary>
public sealed class CommandFile
{
    /// <summary>Nombre de un <see cref="CommandType"/>, sin distinguir mayúsculas.</summary>
    public string Type { get; set; } = "";

    /// <summary>Parámetros de la orden. Null cuando no tiene, para no ensuciar el fichero con un objeto vacío.</summary>
    public Dictionary<string, string>? Parameters { get; set; }
}

/// <summary>Forma serializada de hotkeys.json (spec section 26).</summary>
public sealed class HotkeysFile
{
    /// <summary>Versión del formato. Si es mayor que la que entiende esta compilación, se avisa.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Los atajos tal como se escriben en el fichero.</summary>
    public List<HotkeyEntryFile> Hotkeys { get; set; } = new();
}

/// <summary>Serialization shape of rules.json (spec section 27).</summary>
public sealed class RulesFile
{
    /// <summary>Versión del formato. Si es mayor que la que entiende esta compilación, se avisa.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Las reglas de ventana.</summary>
    public List<WindowRule> Rules { get; set; } = new();
}

/// <summary>A configuration problem worth showing in the UI instead of only in the log.</summary>
public sealed record ConfigurationIssue(string File, string Message);

/// <summary>Conversion between the file shapes and the domain models.</summary>
public static class ConfigurationMapper
{
    /// <summary>
    /// Convierte una entrada del fichero en un atajo, o devuelve null con el motivo en
    /// <paramref name="error"/> si no tiene id o nombra una tecla, un modificador o una orden
    /// desconocidos.
    /// </summary>
    /// <param name="entry">La entrada tal como se leyó del fichero.</param>
    /// <param name="error">Por qué se descartó, en inglés, para el diario y la lista de problemas. Null si se convirtió.</param>
    public static HotkeyDefinition? ToDefinition(HotkeyEntryFile entry, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            error = "A hotkey without an id was ignored.";
            return null;
        }

        if (!Enum.TryParse(entry.Key, ignoreCase: true, out HotkeyKey key) || key == HotkeyKey.None)
        {
            error = $"Hotkey '{entry.Id}': unknown key '{entry.Key}'.";
            return null;
        }

        if (!TryParseModifiers(entry.Modifiers, entry.Id, out HotkeyModifiers modifiers, out error))
        {
            return null;
        }

        // La segunda combinación es opcional entera: sin tecla no hay segunda, y da igual lo que
        // digan sus modificadores.
        HotkeyKey alternateKey = HotkeyKey.None;
        HotkeyModifiers alternateModifiers = HotkeyModifiers.None;

        if (!string.IsNullOrWhiteSpace(entry.AlternateKey))
        {
            if (!Enum.TryParse(entry.AlternateKey, ignoreCase: true, out alternateKey)
                || alternateKey == HotkeyKey.None)
            {
                error = $"Hotkey '{entry.Id}': unknown second key '{entry.AlternateKey}'.";
                return null;
            }

            if (!TryParseModifiers(entry.AlternateModifiers, entry.Id, out alternateModifiers, out error))
            {
                return null;
            }
        }

        if (!Enum.TryParse(entry.Command.Type, ignoreCase: true, out CommandType commandType)
            || commandType == CommandType.None)
        {
            error = $"Hotkey '{entry.Id}': unknown command '{entry.Command.Type}'.";
            return null;
        }

        HotkeyDefinition definition = new()
        {
            Id = entry.Id,
            Modifiers = modifiers,
            Key = key,
            AlternateModifiers = alternateModifiers,
            AlternateKey = alternateKey,
            Enabled = entry.Enabled,
            AllowRepeat = entry.AllowRepeat,
            Command = new CommandDefinition(commandType)
            {
                Parameters = entry.Command.Parameters ?? new Dictionary<string, string>()
            }
        };

        if (!definition.IsValid)
        {
            error = $"Hotkey '{entry.Id}': at least one of Ctrl, Alt or Shift is required.";
            return null;
        }

        return definition;
    }

    /// <summary>Lee una lista de modificadores del fichero, en cualquiera de sus nombres.</summary>
    private static bool TryParseModifiers(
        List<string>? names,
        string hotkeyId,
        out HotkeyModifiers modifiers,
        out string? error)
    {
        modifiers = HotkeyModifiers.None;
        error = null;

        foreach (string raw in names ?? Enumerable.Empty<string>())
        {
            HotkeyModifiers parsed = raw.Trim().ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None
            };

            if (parsed == HotkeyModifiers.None)
            {
                error = $"Hotkey '{hotkeyId}': unknown modifier '{raw}'.";
                return false;
            }

            modifiers |= parsed;
        }

        return true;
    }

    /// <summary>Los modificadores tal y como se escriben en el fichero.</summary>
    private static List<string> ModifierNames(HotkeyModifiers modifiers)
    {
        List<string> names = new();

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            names.Add("Control");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            names.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            names.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            names.Add("Windows");
        }

        return names;
    }

    /// <summary>
    /// Convierte un atajo en su forma de fichero. Lo que no se usa (segunda combinación,
    /// parámetros) queda en null para que no aparezca en el JSON.
    /// </summary>
    public static HotkeyEntryFile ToFile(HotkeyDefinition definition)
    {
        return new HotkeyEntryFile
        {
            Id = definition.Id,
            Modifiers = ModifierNames(definition.Modifiers),
            Key = definition.Key.ToString(),

            // Nulos cuando no hay segunda combinación, para que el fichero de quien no la use
            // quede exactamente como estaba.
            AlternateModifiers = definition.HasAlternate ? ModifierNames(definition.AlternateModifiers) : null,
            AlternateKey = definition.HasAlternate ? definition.AlternateKey.ToString() : null,

            Enabled = definition.Enabled,
            AllowRepeat = definition.AllowRepeat,
            Command = new CommandFile
            {
                Type = definition.Command.Type.ToString(),
                Parameters = definition.Command.Parameters.Count == 0 ? null : definition.Command.Parameters
            }
        };
    }
}
