namespace VirtualDeskManager.Core.Desktops;

/// <summary>A rectangle in physical pixels, relative to the client area of the overlay.</summary>
/// <remarks>
/// Deliberately not <c>System.Drawing.Rectangle</c> nor <c>System.Windows.Rect</c>: Core must
/// stay free of both, and the numbers travel straight into DWM, which only speaks integer
/// pixels.
/// </remarks>
public readonly record struct LayoutRect(int Left, int Top, int Width, int Height)
{
    /// <summary>First column past the right edge (exclusive).</summary>
    public int Right => Left + Width;

    /// <summary>First row past the bottom edge (exclusive).</summary>
    public int Bottom => Top + Height;

    /// <summary>True when there is no area to draw into.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Whether the point falls inside; the right and bottom edges are outside.</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>Shrinks the rectangle on every side, never past nothing.</summary>
    public LayoutRect Deflate(int amount)
    {
        int width = Width - amount * 2;
        int height = Height - amount * 2;

        return width <= 0 || height <= 0
            ? new LayoutRect(Left, Top, 0, 0)
            : new LayoutRect(Left + amount, Top + amount, width, height);
    }
}

/// <summary>Spacing of the overview, in physical pixels.</summary>
public sealed class DesktopGridLayoutOptions
{
    /// <summary>Free space between the overview and the edges of the screen.</summary>
    public int Padding { get; init; } = 24;

    /// <summary>Free space between one desktop column and the next.</summary>
    public int ColumnGap { get; init; } = 16;

    /// <summary>Free space around each window inside a column.</summary>
    public int CellGap { get; init; } = 10;

    /// <summary>Band above each column holding the desktop name.</summary>
    public int HeaderHeight { get; init; } = 44;

    /// <summary>
    /// Band under each thumbnail holding the icon and the title.
    /// </summary>
    /// <remarks>
    /// It cannot be placed over the thumbnail: DWM composes thumbnails above everything the
    /// window itself paints, so anything drawn there is invisible.
    /// </remarks>
    public int CaptionHeight { get; init; } = 26;

    /// <summary>Band along the top for windows that live on every desktop. Zero hides it.</summary>
    public int PinnedStripHeight { get; init; } = 132;

    /// <summary>A thumbnail smaller than this in either direction is not worth drawing.</summary>
    public int MinimumThumbnailSide { get; init; } = 32;

    /// <summary>Lado del botón de cerrar, arriba a la derecha de cada miniatura.</summary>
    /// <remarks>
    /// Reservado <b>fuera</b> de la miniatura y no encima, y no es un capricho de diseño: una
    /// miniatura de DWM tapa todo lo que la ventana dibuje debajo, así que una cruz pintada sobre
    /// ella sería sencillamente invisible. El hueco tiene que existir en la distribución.
    /// </remarks>
    public int CloseButtonSide { get; init; } = 22;

    /// <summary>
    /// Ancho de la columna «+ Nuevo escritorio», a la derecha de todas. Cero: no hay.
    /// </summary>
    public int NewDesktopColumnWidth { get; init; }
}

/// <summary>Where one window goes: the whole cell, the thumbnail inside it and its caption.</summary>
public sealed record GridCellLayout(
    int Index,
    LayoutRect Cell,
    LayoutRect Thumbnail,
    LayoutRect Caption,
    LayoutRect Close);

/// <summary>Where one desktop goes: its title band, its body and the windows inside it.</summary>
public sealed record GridColumnLayout(
    int DesktopIndex,
    LayoutRect Header,
    LayoutRect Body,
    IReadOnlyList<GridCellLayout> Cells);

/// <summary>The complete overview.</summary>
/// <param name="PinnedStrip">La franja de las ventanas que están en todos los escritorios; vacía si no hay.</param>
/// <param name="PinnedCells">Dónde va cada una de esas ventanas.</param>
/// <param name="Columns">Una por escritorio.</param>
/// <param name="NewDesktop">La columna «+ Nuevo escritorio»; vacía si no se pidió o no cabe.</param>
public sealed record DesktopGridLayoutResult(
    LayoutRect PinnedStrip,
    IReadOnlyList<GridCellLayout> PinnedCells,
    IReadOnlyList<GridColumnLayout> Columns,
    LayoutRect NewDesktop = default);

/// <summary>
/// Splits the screen into one vertical zone per virtual desktop and places every window inside
/// its zone, big enough to be recognisable and small enough that they all fit.
/// </summary>
/// <remarks>
/// Pure arithmetic on purpose, with no Win32 anywhere, so the awkward cases (a desktop with no
/// windows, more windows than pixels, a single desktop) are covered by unit tests instead of by
/// opening the overlay and squinting at it.
/// </remarks>
public static class DesktopGridLayout
{
    /// <summary>Fallback shape for a window whose real size could not be measured.</summary>
    private const double DefaultAspectRatio = 16.0 / 9.0;

    /// <summary>
    /// Computes the overview.
    /// </summary>
    /// <param name="width">Client width of the overlay, in physical pixels.</param>
    /// <param name="height">Client height of the overlay, in physical pixels.</param>
    /// <param name="columns">
    /// One entry per desktop, in the order Task View shows them, holding the width/height ratio
    /// of each of its windows.
    /// </param>
    /// <param name="pinned">Ratios of the windows that live on every desktop. May be empty.</param>
    /// <param name="options">Padding, gaps and other spacing. Null means the defaults.</param>
    public static DesktopGridLayoutResult Compute(
        int width,
        int height,
        IReadOnlyList<IReadOnlyList<double>> columns,
        IReadOnlyList<double>? pinned = null,
        DesktopGridLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(columns);

        options ??= new DesktopGridLayoutOptions();
        pinned ??= Array.Empty<double>();

        LayoutRect available = new LayoutRect(0, 0, Math.Max(0, width), Math.Max(0, height))
            .Deflate(options.Padding);

        if (available.IsEmpty || columns.Count == 0)
        {
            return new DesktopGridLayoutResult(
                new LayoutRect(0, 0, 0, 0),
                Array.Empty<GridCellLayout>(),
                Array.Empty<GridColumnLayout>());
        }

        // The pinned strip only takes room when there is something to put in it.
        LayoutRect pinnedStrip = new(0, 0, 0, 0);
        IReadOnlyList<GridCellLayout> pinnedCells = Array.Empty<GridCellLayout>();

        if (pinned.Count > 0 && options.PinnedStripHeight > 0 && available.Height > options.PinnedStripHeight * 2)
        {
            pinnedStrip = new LayoutRect(available.Left, available.Top, available.Width, options.PinnedStripHeight);
            pinnedCells = Place(pinnedStrip, pinned, options);

            available = new LayoutRect(
                available.Left,
                pinnedStrip.Bottom + options.ColumnGap,
                available.Width,
                available.Height - pinnedStrip.Height - options.ColumnGap);
        }

        // La columna para crear otro escritorio va a la derecha de todas, con la misma altura, y
        // sólo si deja sitio de sobra para las de verdad: con una pantalla estrecha y muchos
        // escritorios, ver las ventanas importa más que el atajo para crear.
        LayoutRect newDesktop = new(0, 0, 0, 0);
        int reserve = options.NewDesktopColumnWidth > 0 ? options.NewDesktopColumnWidth + options.ColumnGap : 0;

        if (reserve > 0 && available.Width - reserve >= columns.Count * options.NewDesktopColumnWidth)
        {
            newDesktop = new LayoutRect(available.Right - options.NewDesktopColumnWidth, available.Top, options.NewDesktopColumnWidth, available.Height);
            available = new LayoutRect(available.Left, available.Top, available.Width - reserve, available.Height);
        }

        List<GridColumnLayout> result = new(columns.Count);

        // Integer division would drop up to one pixel per column on the right edge. Deriving both
        // edges from the same rounding instead keeps the columns flush with the screen.
        double columnWidth = (available.Width - options.ColumnGap * (double)(columns.Count - 1)) / columns.Count;

        for (int index = 0; index < columns.Count; index++)
        {
            int left = available.Left + (int)Math.Round(index * (columnWidth + options.ColumnGap));
            int right = available.Left + (int)Math.Round(index * (columnWidth + options.ColumnGap) + columnWidth);

            LayoutRect header = new(left, available.Top, right - left, options.HeaderHeight);

            LayoutRect body = new(
                left,
                available.Top + options.HeaderHeight,
                right - left,
                available.Height - options.HeaderHeight);

            result.Add(new GridColumnLayout(index, header, body, Place(body, columns[index], options)));
        }

        return new DesktopGridLayoutResult(pinnedStrip, pinnedCells, result, newDesktop);
    }

    /// <summary>
    /// Arranges <paramref name="ratios"/> inside <paramref name="area"/> so that the thumbnails
    /// come out as large as possible.
    /// </summary>
    /// <remarks>
    /// A plain vertical list wastes most of a tall column once there are three or four windows in
    /// it: each one gets a short, wide slot while the windows themselves are roughly as tall as
    /// the screen, so the thumbnail shrinks to fit the height and leaves the width empty. Trying
    /// every column count and keeping whichever yields the largest thumbnail costs nothing at
    /// these sizes and answers "one column" on its own whenever the zone really is narrow.
    /// </remarks>
    private static IReadOnlyList<GridCellLayout> Place(
        LayoutRect area,
        IReadOnlyList<double> ratios,
        DesktopGridLayoutOptions options)
    {
        if (ratios.Count == 0 || area.IsEmpty)
        {
            return Array.Empty<GridCellLayout>();
        }

        int bestColumns = 1;
        double bestArea = -1;

        for (int candidate = 1; candidate <= ratios.Count; candidate++)
        {
            int rows = (int)Math.Ceiling(ratios.Count / (double)candidate);

            int cellWidth = (area.Width - options.CellGap * (candidate - 1)) / candidate;
            int cellHeight = (area.Height - options.CellGap * (rows - 1)) / rows;

            if (cellWidth <= 0 || cellHeight <= options.CaptionHeight)
            {
                continue;
            }

            // The worst window decides: a tiling is only as good as its smallest thumbnail.
            double smallest = double.MaxValue;

            foreach (double ratio in ratios)
            {
                LayoutRect thumbnail = Fit(
                    new LayoutRect(0, 0, cellWidth, cellHeight - options.CaptionHeight),
                    ratio);

                smallest = Math.Min(smallest, (double)thumbnail.Width * thumbnail.Height);
            }

            if (smallest > bestArea)
            {
                bestArea = smallest;
                bestColumns = candidate;
            }
        }

        int columns = bestColumns;
        int rowCount = (int)Math.Ceiling(ratios.Count / (double)columns);

        double slotWidth = (area.Width - options.CellGap * (double)(columns - 1)) / columns;
        int uniformHeight = (area.Height - options.CellGap * (rowCount - 1)) / rowCount;

        // How tall each window comes out at that width. Rows are then given the height their own
        // windows actually need instead of an equal share of the zone: a row of wide, short
        // windows would otherwise sit marooned in the middle of a tall empty slot, with its
        // captions stranded far below the pictures they name.
        int[] heights = new int[ratios.Count];

        for (int index = 0; index < ratios.Count; index++)
        {
            heights[index] = Fit(
                new LayoutRect(0, 0, (int)Math.Round(slotWidth), uniformHeight - options.CaptionHeight),
                ratios[index]).Height;
        }

        int[] rowHeights = new int[rowCount];

        for (int index = 0; index < ratios.Count; index++)
        {
            int row = index / columns;
            rowHeights[row] = Math.Max(rowHeights[row], heights[index] + options.CaptionHeight);
        }

        int used = rowHeights.Sum() + options.CellGap * (rowCount - 1);

        // Whatever is left over goes half above and half below, so a zone holding one window
        // shows it in the middle rather than pinned to the top.
        int top = area.Top + Math.Max(0, (area.Height - used) / 2);

        List<GridCellLayout> cells = new(ratios.Count);

        for (int index = 0; index < ratios.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;

            int left = area.Left + (int)Math.Round(column * (slotWidth + options.CellGap));

            int y = top;

            for (int previous = 0; previous < row; previous++)
            {
                y += rowHeights[previous] + options.CellGap;
            }

            LayoutRect cell = new(left, y, (int)Math.Round(slotWidth), rowHeights[row]);

            LayoutRect caption = new(
                cell.Left,
                cell.Bottom - options.CaptionHeight,
                cell.Width,
                options.CaptionHeight);

            // Sitting on top of the caption rather than centred above it, so that the name is
            // always right under the picture it belongs to and never floating in open space.
            // Se le quita a la miniatura la altura del botón de cerrar, siempre.
            //
            // La cruz va encima de la miniatura, a su derecha, y ahí no puede haber nada que
            // dibuje la ventana: DWM compone la miniatura por encima de su rectángulo entero, así
            // que una cruz pintada dentro sería invisible.
            //
            // Y se reserva sin preguntar porque preguntar no servía de nada: la altura de cada
            // fila sale del contenido, de modo que la celda se ajusta a la miniatura y encima no
            // sobra sitio prácticamente nunca. Medido, la rama condicional no se tomaba jamás.
            LayoutRect thumbnail = BottomAligned(
                cell,
                caption,
                cell.Height - options.CaptionHeight - options.CloseButtonSide,
                ratios[index]);

            if (thumbnail.Width < options.MinimumThumbnailSide || thumbnail.Height < options.MinimumThumbnailSide)
            {
                thumbnail = new LayoutRect(thumbnail.Left, thumbnail.Top, 0, 0);
            }

            // Sin miniatura no hay nada que cerrar desde aquí: el botón se iría a flotar solo.
            LayoutRect close = thumbnail.IsEmpty
                ? new LayoutRect(thumbnail.Left, thumbnail.Top, 0, 0)
                : new LayoutRect(
                    thumbnail.Right - options.CloseButtonSide,
                    thumbnail.Top - options.CloseButtonSide,
                    options.CloseButtonSide,
                    options.CloseButtonSide);

            cells.Add(new GridCellLayout(index, cell, thumbnail, caption, close));
        }

        return cells;
    }

    /// <summary>La miniatura apoyada en el pie de la celda, dentro de la altura que se le deje.</summary>
    private static LayoutRect BottomAligned(LayoutRect cell, LayoutRect caption, int available, double ratio)
    {
        LayoutRect fitted = Fit(new LayoutRect(cell.Left, cell.Top, cell.Width, Math.Max(0, available)), ratio);

        return new LayoutRect(fitted.Left, caption.Top - fitted.Height, fitted.Width, fitted.Height);
    }

    /// <summary>
    /// Largest rectangle of the given width/height ratio that fits centred in <paramref name="slot"/>.
    /// </summary>
    /// <remarks>
    /// DWM stretches a thumbnail to whatever destination it is given, so without this every
    /// window comes out squashed into the shape of its cell. The ratio has to be honoured here,
    /// by us, and never by the compositor.
    /// </remarks>
    public static LayoutRect Fit(LayoutRect slot, double ratio)
    {
        if (slot.IsEmpty)
        {
            return new LayoutRect(slot.Left, slot.Top, 0, 0);
        }

        if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
        {
            ratio = DefaultAspectRatio;
        }

        int width = slot.Width;
        int height = (int)Math.Round(width / ratio);

        if (height > slot.Height)
        {
            height = slot.Height;
            width = (int)Math.Round(height * ratio);
        }

        width = Math.Clamp(width, 0, slot.Width);
        height = Math.Clamp(height, 0, slot.Height);

        return new LayoutRect(
            slot.Left + (slot.Width - width) / 2,
            slot.Top + (slot.Height - height) / 2,
            width,
            height);
    }
}
