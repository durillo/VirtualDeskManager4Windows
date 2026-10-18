namespace VirtualDeskManager.Core.Models;

/// <summary>Una pulsación concreta: unos modificadores y una tecla.</summary>
/// <remarks>
/// Existe desde que un atajo puede tener dos. Mientras sólo había una, los modificadores y la
/// tecla sueltos en <see cref="HotkeyDefinition"/> bastaban; con dos, cada comparación se
/// convertía en cuatro, y el código que decide si dos atajos chocan pasaba a ser un nido de
/// condiciones donde es fácil olvidarse justo del cruce que importa.
/// </remarks>
public readonly record struct HotkeyCombination(HotkeyModifiers Modifiers, HotkeyKey Key)
{
    /// <summary>Ninguna combinación: sin modificadores ni tecla.</summary>
    public static readonly HotkeyCombination None = new(HotkeyModifiers.None, HotkeyKey.None);

    /// <summary>Falso cuando no hay combinación: sin tecla no hay nada que registrar.</summary>
    public bool IsSet => Key != HotkeyKey.None;

    /// <summary>
    /// La misma pulsación sin NoRepeat, que es lo que hay que comparar.
    /// </summary>
    /// <remarks>
    /// NoRepeat cambia cómo se comporta el atajo mientras se mantiene pulsado, no qué pulsación
    /// le reclama a Windows. Dos atajos que sólo se diferencien en eso siguen chocando.
    /// </remarks>
    public HotkeyCombination Normalised => new(Modifiers & ~HotkeyModifiers.NoRepeat, Key);

    /// <summary>Si las dos le pedirían a Windows la misma pulsación.</summary>
    public bool SameAs(HotkeyCombination other) => IsSet && other.IsSet && Normalised == other.Normalised;

    /// <summary>La combinación como la lee el usuario, p. ej. <c>Ctrl + Alt + T</c>. Vacío si no hay.</summary>
    public string DisplayText => IsSet ? HotkeyDefinition.Format(Modifiers, Key) : "";

    /// <summary>Lo mismo que <see cref="DisplayText"/>.</summary>
    public override string ToString() => DisplayText;
}
