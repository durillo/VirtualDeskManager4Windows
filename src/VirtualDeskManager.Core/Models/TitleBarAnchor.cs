namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Where the floating title bar button has to be drawn, in physical screen pixels.
/// </summary>
/// <param name="Handle">Window the button belongs to. Commands target this handle, never the foreground window.</param>
/// <param name="X">Left edge of the button, in physical screen pixels.</param>
/// <param name="Y">Top edge of the button, in physical screen pixels.</param>
/// <param name="Size">Width and height of the (square) button, in physical pixels.</param>
/// <param name="IsTopMost">Whether the window is always on top, so the button has to be as well to stay above it.</param>
public readonly record struct TitleBarAnchor(nint Handle, int X, int Y, int Size, bool IsTopMost)
{
    /// <summary>Ventana, tamaño y posición para el diario.</summary>
    public override string ToString() => $"0x{Handle:X} {Size}px @{X},{Y}";
}

/// <summary>
/// Geometry of the floating button. Pure arithmetic, kept out of the Windows project so it can
/// be unit tested (spec section 40).
/// </summary>
public static class TitleBarLayout
{
    /// <summary>Por debajo de esto el botón ya no se puede pulsar con el ratón.</summary>
    private const int MinimumSize = 10;

    /// <summary>Y por encima deja de parecer un botón de barra de título.</summary>
    private const int MaximumSize = 64;

    /// <summary>Below this the title bar is a sliver and no button fits.</summary>
    private const int MinimumCaptionHeight = 18;

    /// <summary>A window narrower than this has no room left once the caption buttons are there.</summary>
    private const int MinimumCaptionWidth = 220;

    /// <summary>
    /// Fallback width of minimize + maximize + close, relative to the caption height, used only
    /// when the desktop manager will not say where they really are.
    /// </summary>
    /// <remarks>
    /// Measured on Windows 11: the three buttons take 146 pixels over a 23 pixel caption, so the
    /// three of them together are about 6.3 times the caption height. An earlier guess of 4.35
    /// left the button sitting on top of minimize.
    /// </remarks>
    private const double FallbackCaptionButtonsRatio = 6.3;

    /// <summary>
    /// Places the button inside the caption, just to the left of minimize / maximize / close.
    /// Returns null when the window has no usable title bar, and the caller then hides the button.
    /// </summary>
    /// <param name="handle">Window the button belongs to. Zero returns null.</param>
    /// <param name="titleBar">The window's caption, in physical screen pixels. Too short or too narrow returns null.</param>
    /// <param name="isTopMost">Whether the window is always on top; copied into the result.</param>
    /// <param name="extraOffset">User correction, in pixels, for applications that draw their own caption.</param>
    /// <param name="captionButtonsWidth">
    /// Real width of minimize + maximize + close when the desktop manager can be asked for it.
    /// Null falls back to an estimate based on the caption height.
    /// </param>
    /// <param name="sizePercent">User scaling of the button's natural size, from 50 to 200 (clamped); 100 leaves it as is.</param>
    public static TitleBarAnchor? Compute(
        nint handle,
        WindowBounds titleBar,
        bool isTopMost,
        int extraOffset = 0,
        int? captionButtonsWidth = null,
        int sizePercent = 100)
    {
        if (handle == 0
            || titleBar.Height < MinimumCaptionHeight
            || titleBar.Width < MinimumCaptionWidth)
        {
            return null;
        }

        int size = ComputeButtonSize(titleBar.Height, sizePercent);
        int margin = ComputeMargin(size);

        int captionButtons = captionButtonsWidth is int measured && measured > 0
            ? measured
            : (int)Math.Round(titleBar.Height * FallbackCaptionButtonsRatio);

        int x = titleBar.Right - captionButtons - extraOffset - size - margin;
        int y = titleBar.Y + ((titleBar.Height - size) / 2);

        // Never let the button walk over the window icon or the beginning of the title.
        if (x < titleBar.X + size)
        {
            return null;
        }

        return new TitleBarAnchor(handle, x, y, size, isTopMost);
    }

    /// <summary>
    /// Tamaño del botón, con la corrección que el usuario haya pedido.
    /// </summary>
    /// <remarks>
    /// El porcentaje mueve el tamaño natural en vez de sustituirlo, así que al 100 por cien sale
    /// exactamente lo de siempre y nadie ve cambiar su botón por haber añadido este ajuste.
    ///
    /// Y por encima de todo, el botón tiene que caber en la barra de título. Sin ese tope, al
    /// 200 por cien en una barra normal salía un botón más alto que la propia barra: la cuenta de
    /// centrarlo daba una coordenada negativa y el botón asomaba por encima del título, invadiendo
    /// la ventana de al lado.
    /// </remarks>
    public static int ComputeButtonSize(int captionHeight, int sizePercent = 100)
    {
        int natural = Math.Clamp((int)Math.Round(captionHeight * 0.62), 12, 40);
        int scaled = (int)Math.Round(natural * (Math.Clamp(sizePercent, 50, 200) / 100.0));
        int fitsInCaption = Math.Max(MinimumSize, captionHeight - 2);

        return Math.Clamp(scaled, MinimumSize, Math.Min(MaximumSize, fitsInCaption));
    }

    /// <summary>
    /// Separación, en píxeles, entre el botón y los botones de la ventana (minimizar, maximizar,
    /// cerrar): un cuarto del tamaño del botón, nunca menos de 2.
    /// </summary>
    public static int ComputeMargin(int buttonSize) => Math.Max(2, buttonSize / 4);

    /// <summary>
    /// Free caption the button needs: its own width plus a margin on each side. Used to ask the
    /// window for a gap big enough before deciding where to put it.
    /// </summary>
    public static int ComputeRequiredFreeWidth(int captionHeight, int sizePercent = 100)
    {
        int size = ComputeButtonSize(captionHeight, sizePercent);
        return size + (ComputeMargin(size) * 2);
    }
}
