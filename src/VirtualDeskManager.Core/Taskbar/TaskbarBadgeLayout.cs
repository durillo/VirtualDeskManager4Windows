using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Taskbar;

/// <summary>Dónde cae el distintivo dentro del botón de la barra de tareas.</summary>
/// <remarks>
/// Los botones de la barra están pegados unos a otros —44 píxeles de ancho, sin hueco entre
/// ellos— así que un distintivo que se salga por un lado aterriza sobre el botón del vecino y
/// dice una mentira sobre otra aplicación. Por eso todo se recorta contra el botón, y el tamaño
/// que pide el usuario es un deseo y no una orden.
/// </remarks>
public static class TaskbarBadgeLayout
{
    /// <summary>Lado mínimo del distintivo en píxeles, salvo que el botón sea aún más pequeño.</summary>
    public const int MinimumSize = 10;
    /// <summary>Lado máximo del distintivo en píxeles, pida lo que pida el usuario.</summary>
    public const int MaximumSize = 28;

    /// <summary>Margen desde el borde del botón, para que no quede pegado a la esquina.</summary>
    private const int Margin = 2;

    /// <summary>El tamaño del distintivo para un botón de este alto.</summary>
    public static int ComputeSize(int buttonHeight, int sizePercent)
    {
        int natural = Math.Clamp((int)Math.Round(buttonHeight * 0.38), MinimumSize, MaximumSize);
        int scaled = (int)Math.Round(natural * (Math.Clamp(sizePercent, 50, 200) / 100.0));

        return Math.Clamp(scaled, MinimumSize, MaximumSize);
    }

    /// <summary>El rectángulo del distintivo, ya recortado para no invadir al botón vecino.</summary>
    public static WindowBounds Place(WindowBounds button, TaskbarBadgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int size = ComputeSize(button.Height, settings.SizePercent);

        // Antes de colocarlo: en una barra de tareas pequena el distintivo puede no caber ni a su
        // tamano minimo, y entonces manda el boton.
        size = Math.Min(size, Math.Min(button.Width, button.Height));

        int hueco = Math.Max(0, Math.Min(Margin, (Math.Min(button.Width, button.Height) - size) / 2));

        bool derecha = settings.Corner is BadgeCorner.TopRight or BadgeCorner.BottomRight;
        bool abajo = settings.Corner is BadgeCorner.BottomLeft or BadgeCorner.BottomRight;

        int x = derecha ? button.Right - size - hueco : button.X + hueco;
        int y = abajo ? button.Bottom - size - hueco : button.Y + hueco;

        // Recortado contra el boton y no contra la pantalla: lo que hay al lado es otro boton, y
        // marcar la aplicacion equivocada es peor que no marcar ninguna.
        x = Math.Clamp(x, button.X, Math.Max(button.X, button.Right - size));
        y = Math.Clamp(y, button.Y, Math.Max(button.Y, button.Bottom - size));

        return new WindowBounds(x, y, size, size);
    }
}
