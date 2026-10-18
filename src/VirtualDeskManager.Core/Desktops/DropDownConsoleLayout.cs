using VirtualDeskManager.Core.Configuration;

namespace VirtualDeskManager.Core.Desktops;

/// <summary>Los bordes de la consola desplegable que se pueden arrastrar para cambiarle el tamaño.</summary>
public enum ConsoleResizeEdge
{
    /// <summary>El lado izquierdo: cambia el ancho.</summary>
    Left,

    /// <summary>El lado derecho: cambia el ancho.</summary>
    Right,

    /// <summary>El borde de abajo: cambia el alto.</summary>
    Bottom,

    /// <summary>La esquina de abajo a la izquierda: los dos a la vez.</summary>
    BottomLeft,

    /// <summary>La esquina de abajo a la derecha: los dos a la vez.</summary>
    BottomRight
}

/// <summary>
/// Where the drop down console sits at any moment of its slide.
/// </summary>
/// <remarks>
/// Quake did not draw the console at its final height and leave it there: it kept a current
/// height that chased the target one at <c>scr_conspeed</c> pixels per second, and redrew
/// wherever that had got to. The same idea here, except the caller owns the clock and this only
/// answers "given that you are this far through, here is the rectangle".
///
/// Pure arithmetic, in physical pixels, so the awkward parts (a console taller than the screen,
/// a monitor that is not at the origin, a duration of zero) are settled by tests.
/// </remarks>
public static class DropDownConsoleLayout
{
    /// <summary>
    /// The rectangle the console occupies when it is all the way down.
    /// </summary>
    /// <param name="screen">The monitor it drops into, in physical pixels.</param>
    /// <param name="settings">Width and height of the console, as percentages of the monitor. Must not be null.</param>
    public static LayoutRect Target(LayoutRect screen, DropDownConsoleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int width = (int)Math.Round(screen.Width * (settings.WidthPercent / 100.0));
        int height = (int)Math.Round(screen.Height * (settings.HeightPercent / 100.0));

        width = Math.Clamp(width, 1, screen.Width);
        height = Math.Clamp(height, 1, screen.Height);

        // Centred horizontally and glued to the top edge: it comes down from up there, so any
        // other vertical position would look like it had been dropped rather than pulled out.
        return new LayoutRect(screen.Left + (screen.Width - width) / 2, screen.Top, width, height);
    }

    /// <summary>
    /// Where the console is when it is <paramref name="progress"/> of the way down, from 0 (out
    /// of sight above the screen) to 1 (fully down).
    /// </summary>
    /// <remarks>
    /// The console keeps its full height throughout and is simply higher up, with the part above
    /// the screen edge clipped by the monitor. Growing it instead would reflow the terminal on
    /// every frame, which means the shell being told its window changed size a dozen times per
    /// slide, and text rewrapping all the way down.
    /// </remarks>
    public static LayoutRect At(LayoutRect screen, DropDownConsoleSettings settings, double progress)
    {
        LayoutRect target = Target(screen, settings);

        progress = double.IsNaN(progress) ? 1 : Math.Clamp(progress, 0, 1);

        int travel = (int)Math.Round(target.Height * (1 - Ease(progress)));

        return new LayoutRect(target.Left, target.Top - travel, target.Width, target.Height);
    }

    /// <summary>
    /// Dónde va la pestaña del pin: colgando del borde de abajo de la consola, a la derecha.
    /// </summary>
    /// <remarks>
    /// Fuera de la consola y no encima: la consola es una ventana de otro proceso que sube al
    /// frente cada vez que se hace clic en ella, y taparía una pestaña puesta dentro. Además así no
    /// tapa ni texto ni la barra de desplazamiento. Si la consola llega hasta abajo de la pantalla y
    /// no cabe debajo, se mete dentro, en la esquina de abajo a la derecha, antes que perderse.
    /// </remarks>
    /// <param name="console">Dónde está la consola en este momento de la animación.</param>
    /// <param name="screen">La pantalla en la que baja.</param>
    /// <param name="size">Lado de la pestaña, en píxeles físicos.</param>
    public static LayoutRect PinTab(LayoutRect console, LayoutRect screen, int size)
    {
        size = Math.Max(1, size);

        // Separada del borde derecho lo mismo que mide, para no quedar pegada a la esquina.
        int left = Math.Max(console.Left, console.Right - size * 2);
        int top = console.Bottom;

        if (top + size > screen.Bottom)
        {
            top = console.Bottom - size;
        }

        return new LayoutRect(left, top, size, size);
    }

    /// <summary>
    /// Dónde van las tiras para cambiar el tamaño de la consola con el ratón: a los lados, abajo y
    /// en las dos esquinas de abajo. Arriba no: la consola va pegada al borde de la pantalla.
    /// </summary>
    /// <remarks>
    /// Por fuera de la consola, como la pestaña del pin y por lo mismo: dentro las taparía la propia
    /// consola, que es de otro proceso, en cuanto se hiciera clic en ella. Si un borde está pegado
    /// al de la pantalla (consola al 100 %), su tira se mete dentro para no quedarse fuera de ella.
    /// </remarks>
    /// <param name="console">Dónde está la consola.</param>
    /// <param name="screen">La pantalla en la que está.</param>
    /// <param name="thickness">Grosor de las tiras, en píxeles físicos.</param>
    public static IReadOnlyDictionary<ConsoleResizeEdge, LayoutRect> ResizeGrips(LayoutRect console, LayoutRect screen, int thickness)
    {
        int t = Math.Max(1, thickness);

        int left = console.Left - t >= screen.Left ? console.Left - t : console.Left;
        int right = console.Right + t <= screen.Right ? console.Right : console.Right - t;
        int bottom = console.Bottom + t <= screen.Bottom ? console.Bottom : console.Bottom - t;

        // Las esquinas, más grandes que el grosor: un cuadrado de un píxel de lado no hay quien lo
        // acierte con el ratón.
        int corner = t * 3;

        return new Dictionary<ConsoleResizeEdge, LayoutRect>
        {
            [ConsoleResizeEdge.Left] = new(left, console.Top, t, Math.Max(1, console.Height - corner)),
            [ConsoleResizeEdge.Right] = new(right, console.Top, t, Math.Max(1, console.Height - corner)),
            [ConsoleResizeEdge.Bottom] = new(console.Left + corner, bottom, Math.Max(1, console.Width - corner * 2), t),
            [ConsoleResizeEdge.BottomLeft] = new(left, bottom + t - corner, corner, corner),
            [ConsoleResizeEdge.BottomRight] = new(right + t - corner, bottom + t - corner, corner, corner)
        };
    }

    /// <summary>
    /// El ancho y el alto, en porcentaje de la pantalla, después de arrastrar un borde.
    /// </summary>
    /// <remarks>
    /// La consola va centrada, así que al arrastrar un lado crece por los dos a la vez: el ancho
    /// cambia el doble de lo que se mueve el ratón, y así el borde que se arrastra sigue al ratón.
    /// Abajo, el alto cambia lo mismo que se mueve. Siempre entre 10 y 100, como en los ajustes.
    /// </remarks>
    /// <param name="screen">La pantalla en la que está la consola.</param>
    /// <param name="start">Dónde estaba la consola al empezar a arrastrar.</param>
    /// <param name="edge">El borde que se arrastra.</param>
    /// <param name="dx">Lo que se ha movido el ratón en horizontal desde que empezó, en píxeles.</param>
    /// <param name="dy">Lo que se ha movido en vertical.</param>
    public static (int WidthPercent, int HeightPercent) Resize(
        LayoutRect screen,
        LayoutRect start,
        ConsoleResizeEdge edge,
        int dx,
        int dy)
    {
        int width = edge switch
        {
            ConsoleResizeEdge.Right or ConsoleResizeEdge.BottomRight => start.Width + dx * 2,
            ConsoleResizeEdge.Left or ConsoleResizeEdge.BottomLeft => start.Width - dx * 2,
            _ => start.Width
        };

        int height = edge is ConsoleResizeEdge.Bottom or ConsoleResizeEdge.BottomLeft or ConsoleResizeEdge.BottomRight
            ? start.Height + dy
            : start.Height;

        return (Percent(width, screen.Width), Percent(height, screen.Height));
    }

    private static int Percent(int size, int total) =>
        total <= 0 ? 100 : Math.Clamp((int)Math.Round(size * 100.0 / total), 10, 100);

    /// <summary>
    /// Fraction of the slide that has elapsed.
    /// </summary>
    /// <param name="elapsed">Time since the slide started.</param>
    /// <param name="settings">Where the duration comes from.</param>
    /// <param name="opening">True while coming down, false while going back up.</param>
    public static double Progress(TimeSpan elapsed, DropDownConsoleSettings settings, bool opening)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // A duration of zero is a legitimate setting: it means "no animation, just be there".
        double fraction = settings.AnimationMilliseconds <= 0
            ? 1
            : Math.Clamp(elapsed.TotalMilliseconds / settings.AnimationMilliseconds, 0, 1);

        return opening ? fraction : 1 - fraction;
    }

    /// <summary>
    /// Slows the movement as it arrives instead of stopping dead.
    /// </summary>
    /// <remarks>
    /// A straight line reads as mechanical at these distances and durations. Easing out is what
    /// makes a panel this size feel like it has weight.
    /// </remarks>
    private static double Ease(double progress) => 1 - Math.Pow(1 - progress, 3);
}
