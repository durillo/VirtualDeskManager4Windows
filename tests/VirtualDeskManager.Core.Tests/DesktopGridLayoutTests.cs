using VirtualDeskManager.Core.Desktops;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Placement of the desktop overview: one vertical zone per virtual desktop.</summary>
public class DesktopGridLayoutTests
{
    /// <summary>The screen the overview was designed against.</summary>
    private const int ScreenWidth = 3440;

    private const int ScreenHeight = 1440;

    /// <summary>A maximised window on that screen.</summary>
    private const double Maximised = 1720.0 / 1392.0;

    private static IReadOnlyList<IReadOnlyList<double>> Columns(params int[] windowsPerDesktop)
    {
        List<IReadOnlyList<double>> columns = new();

        foreach (int count in windowsPerDesktop)
        {
            columns.Add(Enumerable.Repeat(Maximised, count).ToList());
        }

        return columns;
    }

    [Fact]
    public void The_screen_is_split_into_one_zone_per_desktop()
    {
        foreach (int desktops in new[] { 1, 2, 4, 6, 9 })
        {
            DesktopGridLayoutResult layout = DesktopGridLayout.Compute(
                ScreenWidth,
                ScreenHeight,
                Columns(Enumerable.Repeat(2, desktops).ToArray()));

            Assert.Equal(desktops, layout.Columns.Count);
        }
    }

    [Fact]
    public void The_zones_do_not_overlap_and_stay_on_screen()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(3, 1, 4, 2, 2, 1));

        for (int i = 0; i < layout.Columns.Count; i++)
        {
            LayoutRect body = layout.Columns[i].Body;

            Assert.True(body.Left >= 0, $"zone {i} starts off screen at {body.Left}");
            Assert.True(body.Right <= ScreenWidth, $"zone {i} ends at {body.Right}, past {ScreenWidth}");

            if (i > 0)
            {
                Assert.True(
                    body.Left >= layout.Columns[i - 1].Body.Right,
                    $"zone {i} overlaps the previous one");
            }
        }
    }

    [Fact]
    public void The_zones_are_all_the_same_width()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(1, 1, 1, 1, 1, 1));

        int narrowest = layout.Columns.Min(c => c.Body.Width);
        int widest = layout.Columns.Max(c => c.Body.Width);

        // Rounding may cost a pixel, never more.
        Assert.True(widest - narrowest <= 1, $"between {narrowest} and {widest} pixels");
    }

    [Fact]
    public void Every_window_gets_a_cell()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(1, 4, 3, 2));

        Assert.Single(layout.Columns[0].Cells);
        Assert.Equal(4, layout.Columns[1].Cells.Count);
        Assert.Equal(3, layout.Columns[2].Cells.Count);
        Assert.Equal(2, layout.Columns[3].Cells.Count);
    }

    [Fact]
    public void A_desktop_with_no_windows_still_has_its_zone()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(2, 0, 3));

        Assert.Equal(3, layout.Columns.Count);
        Assert.Empty(layout.Columns[1].Cells);
        Assert.False(layout.Columns[1].Body.IsEmpty);
    }

    [Fact]
    public void Thumbnails_keep_the_shape_of_the_window()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(2, 2, 2, 2));

        foreach (GridColumnLayout column in layout.Columns)
        {
            foreach (GridCellLayout cell in column.Cells)
            {
                double ratio = cell.Thumbnail.Width / (double)cell.Thumbnail.Height;

                Assert.True(
                    Math.Abs(ratio - Maximised) < 0.02,
                    $"ratio {ratio:F3} instead of {Maximised:F3}");
            }
        }
    }

    [Fact]
    public void Thumbnails_stay_inside_their_cell_and_clear_the_caption()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(3, 5, 1, 2));

        foreach (GridColumnLayout column in layout.Columns)
        {
            foreach (GridCellLayout cell in column.Cells)
            {
                Assert.True(cell.Thumbnail.Left >= cell.Cell.Left);
                Assert.True(cell.Thumbnail.Right <= cell.Cell.Right);
                Assert.True(cell.Thumbnail.Top >= cell.Cell.Top);

                // Anything drawn under a thumbnail is invisible, so the caption band must not
                // be covered by it.
                Assert.True(
                    cell.Thumbnail.Bottom <= cell.Caption.Top,
                    $"the thumbnail reaches {cell.Thumbnail.Bottom}, over a caption starting at {cell.Caption.Top}");
            }
        }
    }

    /// <summary>
    /// A caption floating half a zone below the picture it names reads as belonging to nothing,
    /// so the thumbnail has to sit directly on top of its own caption.
    /// </summary>
    [Fact]
    public void Every_caption_touches_the_bottom_of_its_thumbnail()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(1, 4, 3, 6));

        foreach (GridColumnLayout column in layout.Columns)
        {
            foreach (GridCellLayout cell in column.Cells)
            {
                Assert.Equal(cell.Caption.Top, cell.Thumbnail.Bottom);
            }
        }
    }

    /// <summary>
    /// Rows are given the height their own windows need. Four wide windows stacked two by two
    /// must not leave half the zone as empty bands between them.
    /// </summary>
    [Fact]
    public void Rows_do_not_leave_dead_space_between_them()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(4));

        IReadOnlyList<GridCellLayout> cells = layout.Columns[0].Cells;

        IEnumerable<IGrouping<int, GridCellLayout>> rows = cells.GroupBy(c => c.Cell.Top);

        foreach (IGrouping<int, GridCellLayout> row in rows)
        {
            foreach (GridCellLayout cell in row)
            {
                int slack = cell.Cell.Height - (cell.Thumbnail.Height + cell.Caption.Height);

                Assert.True(slack >= 0, "the contents overflow the cell");
                Assert.True(
                    slack <= cell.Cell.Height / 4,
                    $"{slack} pixels of the {cell.Cell.Height} pixel cell are empty");
            }
        }
    }

    [Fact]
    public void A_lone_window_is_centred_in_its_zone()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(1, 1));

        GridColumnLayout column = layout.Columns[0];
        GridCellLayout cell = column.Cells[0];

        int above = cell.Cell.Top - column.Body.Top;
        int below = column.Body.Bottom - cell.Cell.Bottom;

        Assert.True(Math.Abs(above - below) <= 2, $"{above} above, {below} below");
    }

    [Fact]
    public void Cells_of_one_desktop_do_not_overlap_each_other()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(7));

        IReadOnlyList<GridCellLayout> cells = layout.Columns[0].Cells;

        for (int i = 0; i < cells.Count; i++)
        {
            for (int j = i + 1; j < cells.Count; j++)
            {
                bool apart =
                    cells[i].Cell.Right <= cells[j].Cell.Left ||
                    cells[j].Cell.Right <= cells[i].Cell.Left ||
                    cells[i].Cell.Bottom <= cells[j].Cell.Top ||
                    cells[j].Cell.Bottom <= cells[i].Cell.Top;

                Assert.True(apart, $"cells {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void Cells_stay_inside_their_zone()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(4, 6, 2));

        foreach (GridColumnLayout column in layout.Columns)
        {
            foreach (GridCellLayout cell in column.Cells)
            {
                Assert.True(cell.Cell.Left >= column.Body.Left, "a window escapes its zone on the left");
                Assert.True(cell.Cell.Right <= column.Body.Right + 1, "a window escapes its zone on the right");
                Assert.True(cell.Cell.Top >= column.Body.Top, "a window escapes its zone at the top");
                Assert.True(cell.Cell.Bottom <= column.Body.Bottom + 1, "a window escapes its zone at the bottom");
            }
        }
    }

    /// <summary>
    /// The whole point of the auto-fit: four windows in a tall, narrow zone must not each be
    /// squeezed into a quarter-height slot when two rows of two give a far larger picture.
    /// </summary>
    [Fact]
    public void A_crowded_zone_is_tiled_rather_than_stacked()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(4, 4, 4, 4));

        IReadOnlyList<GridCellLayout> cells = layout.Columns[0].Cells;

        int distinctLefts = cells.Select(c => c.Cell.Left).Distinct().Count();

        Assert.True(distinctLefts > 1, "four windows in a narrow zone were stacked in a single column");
    }

    [Fact]
    public void A_single_window_gets_the_whole_zone()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(1, 1));

        GridCellLayout cell = layout.Columns[0].Cells[0];

        Assert.Equal(layout.Columns[0].Body.Width, cell.Cell.Width);
    }

    /// <summary>
    /// Dropping a window decides the target desktop from the point under the cursor, so a point
    /// on a window must fall inside that window's own zone and inside no other.
    /// </summary>
    [Fact]
    public void A_point_on_a_window_belongs_to_exactly_one_zone()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(2, 5, 1, 3, 4));

        foreach (GridColumnLayout column in layout.Columns)
        {
            foreach (GridCellLayout cell in column.Cells)
            {
                int x = cell.Thumbnail.Left + cell.Thumbnail.Width / 2;
                int y = cell.Thumbnail.Top + cell.Thumbnail.Height / 2;

                GridColumnLayout[] hit = layout.Columns.Where(c => Covers(c, x, y)).ToArray();

                Assert.True(
                    hit.Length == 1,
                    $"the centre of a window of zone {column.DesktopIndex} falls in {hit.Length} zones");

                Assert.Equal(column.DesktopIndex, hit[0].DesktopIndex);
            }
        }
    }

    [Fact]
    public void The_pinned_strip_does_not_overlap_any_zone()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(
            ScreenWidth,
            ScreenHeight,
            Columns(2, 2, 2),
            new[] { Maximised, Maximised });

        foreach (GridCellLayout cell in layout.PinnedCells)
        {
            int x = cell.Thumbnail.Left + cell.Thumbnail.Width / 2;
            int y = cell.Thumbnail.Top + cell.Thumbnail.Height / 2;

            Assert.DoesNotContain(layout.Columns, c => Covers(c, x, y));
        }
    }

    /// <summary>A zone as the drop target sees it: its title band plus its body.</summary>
    private static bool Covers(GridColumnLayout column, int x, int y) =>
        x >= column.Header.Left
        && x < column.Header.Right
        && y >= column.Header.Top
        && y < column.Body.Bottom;

    [Fact]
    public void Pinned_windows_get_their_own_strip_and_push_the_zones_down()
    {
        DesktopGridLayoutResult without = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(2, 2));

        DesktopGridLayoutResult with = DesktopGridLayout.Compute(
            ScreenWidth,
            ScreenHeight,
            Columns(2, 2),
            new[] { Maximised });

        Assert.Single(with.PinnedCells);
        Assert.False(with.PinnedStrip.IsEmpty);
        Assert.True(
            with.Columns[0].Header.Top > without.Columns[0].Header.Top,
            "the strip did not push the zones down");
        Assert.True(
            with.Columns[0].Body.Top >= with.PinnedStrip.Bottom,
            "a zone overlaps the pinned strip");
    }

    [Fact]
    public void With_nothing_pinned_the_strip_takes_no_room()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(2, 2));

        Assert.Empty(layout.PinnedCells);
        Assert.True(layout.PinnedStrip.IsEmpty);
    }

    [Fact]
    public void No_desktops_is_answered_with_an_empty_overview()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns());

        Assert.Empty(layout.Columns);
        Assert.Empty(layout.PinnedCells);
    }

    /// <summary>
    /// Twenty desktops on a laptop screen leave zones a few pixels wide. That must come back as
    /// "nothing worth drawing" rather than as a negative size or a crash.
    /// </summary>
    [Fact]
    public void An_impossible_screen_does_not_produce_broken_rectangles()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(
            320,
            240,
            Columns(Enumerable.Repeat(3, 20).ToArray()));

        foreach (GridColumnLayout column in layout.Columns)
        {
            foreach (GridCellLayout cell in column.Cells)
            {
                Assert.True(cell.Thumbnail.Width >= 0);
                Assert.True(cell.Thumbnail.Height >= 0);
            }
        }
    }

    [Fact]
    public void A_screen_smaller_than_its_own_padding_is_survivable()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(10, 10, Columns(1, 1));

        Assert.Empty(layout.Columns);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_window_with_no_measurable_shape_falls_back_instead_of_vanishing(double ratio)
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(
            ScreenWidth,
            ScreenHeight,
            new IReadOnlyList<double>[] { new[] { ratio } });

        GridCellLayout cell = layout.Columns[0].Cells[0];

        Assert.True(cell.Thumbnail.Width > 0);
        Assert.True(cell.Thumbnail.Height > 0);
    }

    [Fact]
    public void Sin_pedirla_no_hay_columna_para_crear_escritorios()
    {
        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(2, 1));

        Assert.True(layout.NewDesktop.IsEmpty);
    }

    [Fact]
    public void La_columna_para_crear_va_a_la_derecha_sin_pisar_ningun_escritorio()
    {
        DesktopGridLayoutOptions options = new() { NewDesktopColumnWidth = 150 };

        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(ScreenWidth, ScreenHeight, Columns(2, 1, 3), options: options);

        Assert.Equal(150, layout.NewDesktop.Width);
        Assert.Equal(ScreenWidth - options.Padding, layout.NewDesktop.Right);
        Assert.True(layout.Columns[^1].Header.Right + options.ColumnGap <= layout.NewDesktop.Left);
        Assert.Equal(layout.Columns[0].Header.Top, layout.NewDesktop.Top);
    }

    [Fact]
    public void Si_no_cabe_se_queda_sin_columna_para_crear_antes_que_apretar_los_escritorios()
    {
        DesktopGridLayoutOptions options = new() { NewDesktopColumnWidth = 150 };

        DesktopGridLayoutResult layout = DesktopGridLayout.Compute(800, ScreenHeight, Columns(1, 1, 1, 1, 1), options: options);

        Assert.True(layout.NewDesktop.IsEmpty);
        Assert.Equal(5, layout.Columns.Count);
    }
}
