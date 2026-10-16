using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>Dónde se pone el distintivo dentro del botón de la barra de tareas.</summary>
public enum BadgeCorner
{
    /// <summary>Esquina superior izquierda.</summary>
    TopLeft,
    /// <summary>Esquina superior derecha. La de serie.</summary>
    TopRight,
    /// <summary>Esquina inferior izquierda.</summary>
    BottomLeft,
    /// <summary>Esquina inferior derecha.</summary>
    BottomRight
}

/// <summary>
/// El distintivo que dice, encima de cada botón de la barra de tareas, en qué escritorio está
/// abierta esa aplicación.
/// </summary>
/// <remarks>
/// Apagado de serie, porque es el tipo de adorno que o se quiere mucho o estorba, y en una barra
/// de tareas cualquier cosa que se añada compite con lo que ya hay.
/// </remarks>
public sealed class TaskbarBadgeSettings
{
    /// <summary>Si se pintan los distintivos. Apagado de serie.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// No marcar lo que ya está en el escritorio actual.
    /// </summary>
    /// <remarks>
    /// Encendido de serie, y es lo que hace útil la función en vez de ruidosa: así un distintivo
    /// significa siempre <i>"esto no está aquí"</i>. Marcarlo todo dejaría la barra llena de
    /// números que repiten el escritorio en el que ya estás.
    /// </remarks>
    public bool HideWhenCurrent { get; set; } = true;

    /// <summary>En qué esquina del botón va el distintivo. De serie, arriba a la derecha.</summary>
    public BadgeCorner Corner { get; set; } = BadgeCorner.TopRight;

    /// <summary>Tamaño relativo, del 50 al 200 por ciento.</summary>
    public int SizePercent { get; set; } = 100;

    /// <summary>Color de fondo, en <c>#RRGGBB</c>. De serie, azul <c>#2563EB</c>.</summary>
    public string Colour { get; set; } = "#2563EB";

    /// <summary>Opacidad, del 20 al 100 por ciento. 90 de serie.</summary>
    public int OpacityPercent { get; set; } = 90;

    /// <summary>El color ya interpretado; si lo escrito no vale, el gris de <see cref="RgbColour.Parse"/>.</summary>
    public RgbColour ParsedColour => RgbColour.Parse(Colour);

    /// <summary>
    /// Lleva tamaño y opacidad a su rango y deja el color escrito en su forma canónica, o el azul
    /// de serie si no se entiende.
    /// </summary>
    public void Normalise()
    {
        SizePercent = Math.Clamp(SizePercent, 50, 200);
        OpacityPercent = Math.Clamp(OpacityPercent, 20, 100);

        if (!RgbColour.TryParse(Colour, out RgbColour colour))
        {
            Colour = "#2563EB";
        }
        else
        {
            Colour = colour.Text;
        }
    }
}
