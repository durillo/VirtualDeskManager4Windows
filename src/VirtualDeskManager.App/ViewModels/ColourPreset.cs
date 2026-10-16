using System.Collections.Generic;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>Un color de partida para el botón de la barra de título.</summary>
public sealed class ColourPreset
{
    private ColourPreset(string key, string hex)
    {
        Key = key;
        Hex = hex;
    }

    /// <summary>El nombre interno del color (<c>Grey</c>, <c>Blue</c>…), con el que se busca su traducción.</summary>
    public string Key { get; }

    /// <summary>El color en <c>#RRGGBB</c>, que es lo que se guarda en los ajustes al elegirlo.</summary>
    public string Hex { get; }

    /// <summary>El nombre del color en el idioma de la interfaz.</summary>
    public string Display => Loc.Get("Colour." + Key);

    /// <summary>
    /// Los siete de siempre.
    /// </summary>
    /// <remarks>
    /// Elegidos para que se vean sobre barras de título claras y oscuras: nada demasiado pálido
    /// ni demasiado apagado, porque el botón se pinta translúcido y un color flojo desaparece.
    /// </remarks>
    public static IEnumerable<ColourPreset> All()
    {
        yield return new ColourPreset("Grey", "#808080");
        yield return new ColourPreset("Blue", "#3B82F6");
        yield return new ColourPreset("Green", "#22C55E");
        yield return new ColourPreset("Red", "#EF4444");
        yield return new ColourPreset("Orange", "#F59E0B");
        yield return new ColourPreset("Purple", "#A855F7");
        yield return new ColourPreset("White", "#FFFFFF");
        yield return new ColourPreset("Black", "#202020");
    }

    /// <summary>El nombre traducido, para que el desplegable lo enseñe aunque no tenga plantilla.</summary>
    public override string ToString() => Display;
}
