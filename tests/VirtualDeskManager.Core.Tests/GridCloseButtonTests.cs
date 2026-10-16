using VirtualDeskManager.Core.Desktops;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// La cruz de cerrar de cada ventana en la rejilla de escritorios.
/// </summary>
/// <remarks>
/// Lo único que hay que asegurar aquí, y es la razón de que exista esta geometría, es que el botón
/// <b>no caiga dentro de la miniatura</b>. DWM compone las miniaturas por encima de todo lo que
/// pinte la ventana, así que una cruz dibujada ahí dentro no se vería y el usuario pulsaría a
/// ciegas.
/// </remarks>
public class GridCloseButtonTests
{
    private static IReadOnlyList<GridCellLayout> Cells(params double[] ratios) =>
        DesktopGridLayout.Compute(3440, 1392, new[] { (IReadOnlyList<double>)ratios }).Columns[0].Cells;

    /// <summary>Anchas, altas y cuadradas: las tres reparten el hueco de forma distinta.</summary>
    [Theory]
    [InlineData(16.0 / 9.0)]
    [InlineData(9.0 / 16.0)]
    [InlineData(1.0)]
    [InlineData(21.0 / 9.0)]
    public void The_close_button_never_lands_on_the_thumbnail(double ratio)
    {
        foreach (int count in new[] { 1, 2, 3, 5, 8 })
        {
            double[] ratios = Enumerable.Repeat(ratio, count).ToArray();

            foreach (GridCellLayout cell in Cells(ratios))
            {
                if (cell.Thumbnail.IsEmpty)
                {
                    continue;
                }

                Assert.False(
                    Overlaps(cell.Close, cell.Thumbnail),
                    $"con {count} ventanas de proporción {ratio:0.00} la cruz cae sobre la miniatura");
            }
        }
    }

    [Fact]
    public void The_close_button_sits_on_the_top_right_of_its_thumbnail()
    {
        GridCellLayout cell = Cells(16.0 / 9.0)[0];

        Assert.Equal(cell.Thumbnail.Right, cell.Close.Right);
        Assert.Equal(cell.Thumbnail.Top, cell.Close.Bottom);
    }

    /// <summary>Tiene que caber entero dentro de la celda o quedaría medio fuera de la columna.</summary>
    [Fact]
    public void The_close_button_stays_inside_its_cell()
    {
        foreach (GridCellLayout cell in Cells(9.0 / 16.0, 9.0 / 16.0, 9.0 / 16.0))
        {
            if (cell.Thumbnail.IsEmpty)
            {
                continue;
            }

            Assert.True(cell.Close.Top >= cell.Cell.Top, "la cruz se sale por arriba de la celda");
            Assert.True(cell.Close.Right <= cell.Cell.Right, "la cruz se sale por la derecha");
            Assert.True(cell.Close.Left >= cell.Cell.Left, "la cruz se sale por la izquierda");
        }
    }

    private static GridCellLayout Cell(int desktops, double ratio, int closeSide)
    {
        IReadOnlyList<double>[] columns = Enumerable
            .Repeat((IReadOnlyList<double>)new[] { ratio }, desktops)
            .ToArray();

        return DesktopGridLayout
            .Compute(3440, 1392, columns, null, new DesktopGridLayoutOptions { CloseButtonSide = closeSide })
            .Columns[0].Cells[0];
    }

    /// <summary>
    /// El hueco de la cruz sale de la miniatura, y ése es el precio de tenerla.
    /// </summary>
    /// <remarks>
    /// Se reserva siempre y no sólo cuando hace falta porque, medido, hace falta siempre: la
    /// altura de cada fila se calcula a partir del contenido, así que la celda se ajusta a la
    /// miniatura y encima no sobra sitio prácticamente nunca.
    /// </remarks>
    [Theory]
    [InlineData(1, 16.0 / 9.0)]
    [InlineData(5, 16.0 / 9.0)]
    [InlineData(5, 9.0 / 16.0)]
    public void The_thumbnail_gives_up_the_height_of_the_button(int desktops, double ratio)
    {
        GridCellLayout with = Cell(desktops, ratio, closeSide: 22);
        GridCellLayout without = Cell(desktops, ratio, closeSide: 0);

        Assert.True(
            with.Thumbnail.Height <= without.Thumbnail.Height,
            "reservar el hueco no puede hacer la miniatura mas grande");

        Assert.False(Overlaps(with.Close, with.Thumbnail));
    }

    /// <summary>Y el precio tiene que ser pequeño: veintidós píxeles, no un tercio de la celda.</summary>
    [Fact]
    public void The_price_of_the_button_is_small()
    {
        GridCellLayout with = Cell(desktops: 5, ratio: 16.0 / 9.0, closeSide: 22);
        GridCellLayout without = Cell(desktops: 5, ratio: 16.0 / 9.0, closeSide: 0);

        int lost = without.Thumbnail.Height - with.Thumbnail.Height;

        Assert.InRange(lost, 0, 30);
    }

    /// <summary>Sin miniatura no hay nada que cerrar, y el botón se iría a flotar solo.</summary>
    [Fact]
    public void A_thumbnail_too_small_to_draw_gets_no_button()
    {
        // Muchísimas ventanas en una pantalla pequeña: no caben y la miniatura se anula.
        IReadOnlyList<GridCellLayout> cells = DesktopGridLayout
            .Compute(400, 300, new[] { (IReadOnlyList<double>)Enumerable.Repeat(1.0, 9).ToArray() })
            .Columns[0].Cells;

        foreach (GridCellLayout cell in cells.Where(c => c.Thumbnail.IsEmpty))
        {
            Assert.True(cell.Close.IsEmpty);
        }
    }

    [Fact]
    public void Turning_the_button_off_leaves_the_layout_exactly_as_it_was()
    {
        double[] ratios = { 9.0 / 16.0, 16.0 / 9.0 };

        foreach (GridCellLayout cell in DesktopGridLayout
                     .Compute(3440, 1392, new[] { (IReadOnlyList<double>)ratios }, null,
                         new DesktopGridLayoutOptions { CloseButtonSide = 0 })
                     .Columns[0].Cells)
        {
            Assert.True(cell.Close.IsEmpty);
        }
    }

    private static bool Overlaps(LayoutRect a, LayoutRect b) =>
        !a.IsEmpty && !b.IsEmpty && a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
