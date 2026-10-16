namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Normalized window rectangle. Kept independent from the Win32 RECT structure so that
/// rules and configuration never depend on native types.
/// </summary>
public readonly record struct WindowBounds(int X, int Y, int Width, int Height)
{
    /// <summary>A zero rectangle, used when the real one could not be read.</summary>
    public static readonly WindowBounds Empty = new(0, 0, 0, 0);

    /// <summary>First column past the right edge (<c>X + Width</c>), like Win32 RECT.</summary>
    public int Right => X + Width;

    /// <summary>First row past the bottom edge (<c>Y + Height</c>), like Win32 RECT.</summary>
    public int Bottom => Y + Height;

    /// <summary>True when it has no area: zero or negative width or height.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Size and position for the log, e.g. <c>800x600@10,20</c>.</summary>
    public override string ToString() => $"{Width}x{Height}@{X},{Y}";
}
