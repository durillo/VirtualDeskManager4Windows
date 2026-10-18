using System.Globalization;

namespace VirtualDeskManager.Core.Models;

/// <summary>
/// A command plus its parameters. Serialized inside hotkeys.json and reused by the tray menu,
/// the UI and the rule engine (spec section 7).
/// </summary>
public sealed class CommandDefinition
{
    /// <summary>Which action to run. <see cref="CommandType.None"/> is always rejected.</summary>
    public CommandType Type { get; set; }

    /// <summary>
    /// Parameters as text, the way they are written in the file (for instance <c>desktop</c> or
    /// <c>zone</c>). Read them through the <c>TryGet...</c> methods, which ignore case.
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>An empty command, for the serializer.</summary>
    public CommandDefinition() { }

    /// <summary>A command of the given type with no parameters.</summary>
    public CommandDefinition(CommandType type) => Type = type;

    /// <summary>Case insensitive parameter lookup (the dictionary comes from JSON, so its comparer is unknown).</summary>
    public string? GetString(string key)
    {
        foreach (KeyValuePair<string, string> pair in Parameters)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>Reads an integer parameter, in invariant culture. False when it is missing or not a number.</summary>
    public bool TryGetInt(string key, out int value)
    {
        value = 0;
        string? raw = GetString(key);
        return raw is not null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads a <c>true</c> / <c>false</c> parameter. False when it is missing or is something else.</summary>
    public bool TryGetBool(string key, out bool value)
    {
        value = false;
        string? raw = GetString(key);
        return raw is not null && bool.TryParse(raw, out value);
    }

    /// <summary>Reads a GUID parameter, such as a desktop identifier. False when it is missing or malformed.</summary>
    public bool TryGetGuid(string key, out Guid value)
    {
        value = Guid.Empty;
        string? raw = GetString(key);
        return raw is not null && Guid.TryParse(raw, out value);
    }

    /// <summary>Type and parameters for the log, e.g. <c>MoveWindowToZone(zone=2)</c>.</summary>
    public override string ToString()
    {
        if (Parameters.Count == 0)
        {
            return Type.ToString();
        }

        string parameters = string.Join(", ", Parameters.Select(p => $"{p.Key}={p.Value}"));
        return $"{Type}({parameters})";
    }
}
