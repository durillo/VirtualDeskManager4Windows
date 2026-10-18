using System.Globalization;

namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Un color de interfaz, sin opacidad.
/// </summary>
/// <remarks>
/// Un tipo propio y no el de WPF porque esto vive en Core, que no conoce la interfaz gráfica
/// (regla de arquitectura 1). Y la transparencia va aparte a propósito: el usuario elige un color
/// y elige cuánto se transparenta, que son dos decisiones distintas y se entienden mejor por
/// separado que empotradas en un ARGB de ocho dígitos.
/// </remarks>
public readonly record struct RgbColour(byte R, byte G, byte B)
{
    /// <summary>Gris medio, <c>#808080</c>: lo que se usa cuando un color escrito no se entiende.</summary>
    public static readonly RgbColour Grey = new(0x80, 0x80, 0x80);

    /// <summary>Lo que se escribe en el fichero: <c>#RRGGBB</c>.</summary>
    public string Text => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Luminancia percibida, de 0 a 1.
    /// </summary>
    /// <remarks>
    /// Con los coeficientes de la recomendación BT.601 y no con la media de los tres canales: el
    /// ojo ve el verde mucho más claro que el azul, y promediando, un amarillo y un azul marino
    /// salen igual de "claros". Esto sólo decide de qué color va el dibujo de dentro, pero es
    /// justo el caso donde promediar se nota: el dibujo desaparece sobre el fondo.
    /// </remarks>
    public double Luminance => ((0.299 * R) + (0.587 * G) + (0.114 * B)) / 255.0;

    /// <summary>Si conviene dibujar encima en oscuro. El umbral es el habitual.</summary>
    public bool IsLight => Luminance > 0.55;

    /// <summary>
    /// Lee <c>#RRGGBB</c>, <c>RRGGBB</c>, <c>#RGB</c> o <c>RGB</c>.
    /// </summary>
    /// <remarks>
    /// Las cuatro formas porque el usuario escribe a mano en una caja de texto, y rechazarle
    /// <c>808080</c> por no llevar almohadilla sería quisquilloso sin motivo.
    /// </remarks>
    public static bool TryParse(string? text, out RgbColour colour)
    {
        colour = Grey;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim().TrimStart('#');

        // La forma corta repite cada dígito: #ABC es #AABBCC, como en CSS.
        if (value.Length == 3)
        {
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length != 6
            || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsed))
        {
            return false;
        }

        colour = new RgbColour((byte)((parsed >> 16) & 0xFF), (byte)((parsed >> 8) & 0xFF), (byte)(parsed & 0xFF));
        return true;
    }

    /// <summary>Como <see cref="TryParse"/>, pero devuelve <see cref="Grey"/> en vez de fallar.</summary>
    public static RgbColour Parse(string? text) => TryParse(text, out RgbColour colour) ? colour : Grey;

    /// <summary>El color como <c>#RRGGBB</c>, igual que <see cref="Text"/>.</summary>
    public override string ToString() => Text;
}
