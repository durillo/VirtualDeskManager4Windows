using System.Text;

namespace VirtualDeskManager.Core.Models;

/// <summary>A global hotkey and the command it triggers (spec section 6).</summary>
public sealed class HotkeyDefinition
{
    /// <summary>Identificador único del atajo (sin distinguir mayúsculas), p. ej. <c>desktop-left</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Modificadores de la combinación principal.</summary>
    public HotkeyModifiers Modifiers { get; set; }

    /// <summary>Tecla de la combinación principal. Sin ella el atajo no es válido.</summary>
    public HotkeyKey Key { get; set; }

    /// <summary>
    /// Modificadores de la segunda combinación, la que también dispara esta misma acción.
    /// </summary>
    public HotkeyModifiers AlternateModifiers { get; set; }

    /// <summary>
    /// Tecla de la segunda combinación. <see cref="HotkeyKey.None"/> significa que no hay.
    /// </summary>
    /// <remarks>
    /// Existe para poder lanzar lo mismo de dos maneras: Ctrl+F4 y Ctrl+4 del teclado numérico,
    /// pongamos, según dónde tenga uno la mano. Viene vacía, así que un atajo que no la use se
    /// comporta y se guarda exactamente igual que antes.
    /// </remarks>
    public HotkeyKey AlternateKey { get; set; }

    /// <summary>True cuando esta acción se puede lanzar de dos maneras.</summary>
    public bool HasAlternate => AlternateKey != HotkeyKey.None;

    /// <summary>La combinación principal como una sola pieza, para comparar.</summary>
    public HotkeyCombination Primary => new(Modifiers, Key);

    /// <summary>La segunda combinación como una sola pieza. Sin tecla si no hay.</summary>
    public HotkeyCombination Alternate => new(AlternateModifiers, AlternateKey);

    /// <summary>Las pulsaciones que este atajo reclama: una, dos, o ninguna si está a medias.</summary>
    public IEnumerable<HotkeyCombination> Combinations
    {
        get
        {
            if (Primary.IsSet)
            {
                yield return Primary;
            }

            if (Alternate.IsSet)
            {
                yield return Alternate;
            }
        }
    }

    /// <summary>Si el atajo se registra. Uno desactivado sigue guardado, pero no reclama ninguna tecla.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When false (default) MOD_NOREPEAT is added so holding the keys down does not
    /// re-trigger the command (spec section 6).
    /// </summary>
    public bool AllowRepeat { get; set; }

    /// <summary>La orden que lanza el atajo.</summary>
    public CommandDefinition Command { get; set; } = new();

    /// <summary>
    /// A hotkey needs a key. The modifiers are the user's business.
    /// </summary>
    /// <remarks>
    /// Modifiers used to be compulsory here, on the grounds that a bare key registered globally
    /// is swallowed everywhere on the machine. That is true, and it is still said out loud in the
    /// editor, but it is a warning and not a veto: it is the user's keyboard, and refusing the
    /// combination they asked for taught them nothing about why.
    /// </remarks>
    public bool IsValid => Key != HotkeyKey.None;

    /// <summary>
    /// Turns a key into the label the user sees, when the plain name is not what is printed on
    /// the keycap.
    /// </summary>
    /// <remarks>
    /// Which character a key produces depends on the keyboard layout, and that is a Win32
    /// question Core has no business answering. The interface plugs a resolver in at startup, and
    /// with nothing plugged in the enum name is used, which is correct but less friendly: "Oem5"
    /// where a Spanish keyboard has "º" printed on it.
    /// </remarks>
    public static Func<HotkeyKey, string?>? KeyNameResolver { get; set; }

    /// <summary>
    /// Human readable shortcut, e.g. <c>Ctrl + Alt + T</c> (spec section 30).
    /// </summary>
    /// <remarks>
    /// Con segunda combinación salen las dos separadas por una barra, porque un atajo que se
    /// puede lanzar de dos maneras y sólo enseña una engaña a quien mira la lista.
    /// </remarks>
    public string DisplayText => HasAlternate
        ? Format(Modifiers, Key) + "  /  " + AlternateDisplayText
        : Format(Modifiers, Key);

    /// <summary>La segunda combinación tal y como se lee, o vacío si no hay.</summary>
    public string AlternateDisplayText => HasAlternate ? Format(AlternateModifiers, AlternateKey) : "";

    /// <summary>
    /// Una combinación como la lee el usuario: <c>Ctrl + Alt + Shift + Win + tecla</c>, siempre en
    /// ese orden. La tecla sale con su nombre en el teclado si hay <see cref="KeyNameResolver"/>.
    /// </summary>
    public static string Format(HotkeyModifiers modifiers, HotkeyKey key)
    {
        StringBuilder builder = new();

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            builder.Append("Ctrl + ");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            builder.Append("Alt + ");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            builder.Append("Shift + ");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            builder.Append("Win + ");
        }

        builder.Append(FormatKey(key));
        return builder.ToString();
    }

    private static string FormatKey(HotkeyKey key)
    {
        string? resolved = KeyNameResolver?.Invoke(key);

        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        string name = key.ToString();

        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
        {
            return name[1].ToString();
        }

        // A key with no name of its own: an enum cast straight from a virtual key code prints as
        // a number, and "Key 0xDC" at least says what it is rather than reading as a stray digit
        // next to the modifiers.
        return int.TryParse(name, out int code) ? $"Key 0x{code:X2}" : name;
    }

    /// <summary>Id, combinaciones y orden para el diario.</summary>
    public override string ToString() => $"{Id}: {DisplayText} -> {Command}";
}
