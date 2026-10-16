using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Zones;

/// <summary>
/// Corrige el borde invisible que Windows deja alrededor de casi todas las ventanas.
/// </summary>
/// <remarks>
/// Una ventana de Windows 11 es más grande de lo que se ve. Alrededor del marco visible hay una
/// franja transparente que sirve para agarrar la ventana y redimensionarla, y que <c>GetWindowRect</c>
/// cuenta como parte de la ventana. Medido en este equipo con Visual Studio Code, Edge, Teams y la
/// terminal: <b>siete píxeles a la izquierda, siete a la derecha y siete abajo</b>; arriba, ninguno.
///
/// Por eso colocar una ventana en una zona "sin separación" dejaba catorce píxeles de hueco entre
/// dos ventanas contiguas y siete contra el borde de la pantalla: la zona se respetaba con
/// exactitud, pero lo que se veía quedaba metido hacia dentro.
///
/// La solución es pedirle al gestor de ventanas del escritorio dónde está el marco <i>visible</i>,
/// restarlo del rectángulo real y agrandar el destino en esa misma diferencia, de modo que sea el
/// borde que se ve el que acabe pegado a la zona.
/// </remarks>
public static class WindowFrame
{
    /// <summary>
    /// El rectángulo que hay que pedirle a <c>MoveWindow</c> para que el borde visible caiga
    /// exactamente en <paramref name="target"/>.
    /// </summary>
    /// <param name="target">Dónde se quiere ver la ventana.</param>
    /// <param name="windowRect">Lo que dice <c>GetWindowRect</c>, con el borde invisible dentro.</param>
    /// <param name="visibleRect">Lo que dice el gestor de ventanas del escritorio que se ve.</param>
    public static WindowBounds Adjust(WindowBounds target, WindowBounds windowRect, WindowBounds visibleRect)
    {
        if (windowRect.IsEmpty || visibleRect.IsEmpty)
        {
            return target;
        }

        int left = visibleRect.X - windowRect.X;
        int top = visibleRect.Y - windowRect.Y;
        int right = windowRect.Right - visibleRect.Right;
        int bottom = windowRect.Bottom - visibleRect.Bottom;

        // Márgenes negativos significarían que lo visible es mayor que la ventana, lo cual no
        // tiene sentido y sólo puede venir de una medida a medio hacer mientras la ventana se
        // anima. En ese caso se deja el destino tal cual, que es peor por siete píxeles pero
        // nunca por más.
        if (left < 0 || top < 0 || right < 0 || bottom < 0)
        {
            return target;
        }

        return new WindowBounds(
            target.X - left,
            target.Y - top,
            target.Width + left + right,
            target.Height + top + bottom);
    }
}
