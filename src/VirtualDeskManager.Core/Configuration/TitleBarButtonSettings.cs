using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// Aspecto del botón flotante que se posa en la barra de título de la ventana activa.
/// </summary>
/// <remarks>
/// El encendido y el desplazamiento siguen en <see cref="AppSettings"/> sueltos, donde ya
/// estaban. Moverlos aquí quedaría más ordenado, pero rompería el settings.json de todo el que ya
/// los tenga puestos, y eso es un precio alto por una simetría que sólo se ve leyendo el código.
/// </remarks>
public sealed class TitleBarButtonSettings
{
    /// <summary>Tamaño del botón como porcentaje del que le tocaría por la altura del título.</summary>
    /// <remarks>
    /// Un porcentaje y no un tamaño en píxeles porque el botón tiene que caber en la barra de
    /// título, y ésa mide distinto en cada ventana y en cada pantalla. Un "22 píxeles" que queda
    /// bien en un portátil se sale de la barra en cuanto la ventana la dibuja más estrecha.
    /// </remarks>
    public int SizePercent { get; set; } = 100;

    /// <summary>Color de fondo, en <c>#RRGGBB</c>.</summary>
    public string Colour { get; set; } = "#808080";

    /// <summary>Cuánto se ve el botón cuando no se le está haciendo nada.</summary>
    /// <remarks>
    /// Bajo a propósito de serie: el botón está encima de la ventana de otro programa todo el
    /// rato, y lo que se quiere es tenerlo a mano, no tenerlo delante.
    /// </remarks>
    public int OpacityPercent { get; set; } = 25;

    /// <summary>El color ya interpretado, con el gris de siempre si lo escrito no vale.</summary>
    public RgbColour ParsedColour => RgbColour.Parse(Colour);

    /// <summary>
    /// Opacidad al pasar el ratón por encima: el doble, sin pasarse.
    /// </summary>
    /// <remarks>
    /// Derivada y no configurable. Son tres estados —quieto, señalado y activo— y pedirle al
    /// usuario tres opacidades para algo de veinte píxeles es cobrarle tres decisiones por un
    /// botón. Lo que hace falta es que se distingan, y doblar la opacidad lo consigue.
    /// </remarks>
    public int HoverOpacityPercent => Math.Clamp(OpacityPercent * 2, 40, 100);

    /// <summary>
    /// Lleva el tamaño al 50–200 % y la opacidad al 10–100 %, y reescribe el color en su forma
    /// canónica (gris si no se entiende).
    /// </summary>
    public void Normalise()
    {
        SizePercent = Math.Clamp(SizePercent, 50, 200);
        OpacityPercent = Math.Clamp(OpacityPercent, 10, 100);

        // Lo que no se entienda vuelve al gris, y además se reescribe: así el usuario ve en la
        // caja lo que de verdad está puesto en vez de su errata.
        Colour = ParsedColour.Text;
    }
}
