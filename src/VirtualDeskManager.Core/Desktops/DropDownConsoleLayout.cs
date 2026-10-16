using VirtualDeskManager.Core.Configuration;

namespace VirtualDeskManager.Core.Desktops;

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
