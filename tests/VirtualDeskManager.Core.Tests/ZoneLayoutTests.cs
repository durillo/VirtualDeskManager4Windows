using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Zones;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class ZoneLayoutTests
{
    /// <summary>El área útil del monitor del usuario, sin la barra de tareas.</summary>
    private static readonly WindowBounds Ultrawide = new(0, 0, 3440, 1392);

    private static IReadOnlyList<WindowBounds> Zones(ZoneTemplate template, int count, int gap = 0) =>
        ZoneLayout.Compute(Ultrawide, template, count, gap);

    [Theory]
    [InlineData(ZoneTemplate.Columns, 3)]
    [InlineData(ZoneTemplate.Rows, 4)]
    [InlineData(ZoneTemplate.Grid, 6)]
    [InlineData(ZoneTemplate.PriorityLeft, 3)]
    [InlineData(ZoneTemplate.PriorityRight, 3)]
    public void Every_template_gives_exactly_the_zones_asked_for(ZoneTemplate template, int count)
    {
        Assert.Equal(count, Zones(template, count).Count);
    }

    [Fact]
    public void No_template_is_a_single_zone_covering_the_screen()
    {
        WindowBounds only = Assert.Single(Zones(ZoneTemplate.None, 5));

        Assert.Equal(Ultrawide, only);
    }

    /// <summary>
    /// Dividir 3440 entre tres y multiplicar deja píxeles del monitor sin cubrir por el borde
    /// derecho, y esa raya se ve.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void The_columns_cover_the_screen_edge_to_edge_with_no_gap_left_over(int count)
    {
        IReadOnlyList<WindowBounds> zones = Zones(ZoneTemplate.Columns, count);

        Assert.Equal(Ultrawide.X, zones[0].X);
        Assert.Equal(Ultrawide.Right, zones[^1].Right);

        for (int i = 1; i < zones.Count; i++)
        {
            Assert.Equal(zones[i - 1].Right, zones[i].X);
        }
    }

    [Fact]
    public void The_rows_cover_it_top_to_bottom_the_same_way()
    {
        IReadOnlyList<WindowBounds> zones = Zones(ZoneTemplate.Rows, 3);

        Assert.Equal(Ultrawide.Y, zones[0].Y);
        Assert.Equal(Ultrawide.Bottom, zones[^1].Bottom);
    }

    [Fact]
    public void The_zones_never_overlap_each_other()
    {
        foreach (ZoneTemplate template in new[]
                 {
                     ZoneTemplate.Columns, ZoneTemplate.Rows, ZoneTemplate.Grid,
                     ZoneTemplate.PriorityLeft, ZoneTemplate.PriorityRight
                 })
        {
            for (int count = 2; count <= ZoneLayout.MaximumZones; count++)
            {
                IReadOnlyList<WindowBounds> zones = Zones(template, count);

                for (int a = 0; a < zones.Count; a++)
                {
                    for (int b = a + 1; b < zones.Count; b++)
                    {
                        Assert.False(
                            Overlaps(zones[a], zones[b]),
                            $"{template} con {count} zonas: la {a + 1} y la {b + 1} se solapan");
                    }
                }
            }
        }
    }

    [Fact]
    public void No_zone_ever_falls_outside_the_screen()
    {
        foreach (ZoneTemplate template in Enum.GetValues<ZoneTemplate>())
        {
            for (int count = 1; count <= ZoneLayout.MaximumZones; count++)
            {
                foreach (WindowBounds zone in Zones(template, count))
                {
                    Assert.True(zone.X >= Ultrawide.X, $"{template}/{count}: se sale por la izquierda");
                    Assert.True(zone.Y >= Ultrawide.Y, $"{template}/{count}: se sale por arriba");
                    Assert.True(zone.Right <= Ultrawide.Right, $"{template}/{count}: se sale por la derecha");
                    Assert.True(zone.Bottom <= Ultrawide.Bottom, $"{template}/{count}: se sale por abajo");
                    Assert.False(zone.IsEmpty, $"{template}/{count}: zona vacia");
                }
            }
        }
    }

    /// <summary>
    /// Cinco zonas entran en una cuadrícula de tres por dos y sobra un hueco. Un agujero en una
    /// esquina de la pantalla no es una zona, es una zona que falta.
    /// </summary>
    [Fact]
    public void An_odd_grid_leaves_no_hole_in_the_corner()
    {
        IReadOnlyList<WindowBounds> zones = Zones(ZoneTemplate.Grid, 5);

        Assert.Equal(5, zones.Count);
        Assert.Equal(Ultrawide.Right, zones[^1].Right);
    }

    [Fact]
    public void The_priority_zone_is_the_big_one_and_is_on_the_side_it_says()
    {
        IReadOnlyList<WindowBounds> left = Zones(ZoneTemplate.PriorityLeft, 3);
        IReadOnlyList<WindowBounds> right = Zones(ZoneTemplate.PriorityRight, 3);

        Assert.Equal(Ultrawide.X, left[0].X);
        Assert.True(left[0].Width > left[1].Width);

        Assert.Equal(Ultrawide.Right, right[0].Right);
        Assert.True(right[0].Width > right[1].Width);
    }

    /// <summary>Medio hueco por lado, para que entre dos zonas quede el pedido y no el doble.</summary>
    [Fact]
    public void The_gap_between_two_zones_is_the_one_asked_for()
    {
        IReadOnlyList<WindowBounds> zones = Zones(ZoneTemplate.Columns, 3, gap: 20);

        Assert.Equal(20, zones[1].X - zones[0].Right);
    }

    [Fact]
    public void An_absurd_gap_never_leaves_a_zone_unusable()
    {
        foreach (WindowBounds zone in Zones(ZoneTemplate.Grid, 9, gap: ZoneLayout.MaximumGap))
        {
            Assert.True(zone.Width > 40 && zone.Height > 40);
        }
    }

    [Fact]
    public void Asking_for_more_zones_than_make_sense_is_brought_back_into_range()
    {
        Assert.Equal(ZoneLayout.MaximumZones, Zones(ZoneTemplate.Columns, 500).Count);
        Assert.Single(Zones(ZoneTemplate.Columns, -3));
    }

    [Fact]
    public void A_screen_of_no_size_gives_no_zones_instead_of_throwing()
    {
        Assert.Empty(ZoneLayout.Compute(new WindowBounds(0, 0, 0, 0), ZoneTemplate.Columns, 3));
    }

    // ------------------------------------------------------------------ localizar

    [Fact]
    public void The_zone_a_window_is_in_is_the_one_it_covers_most()
    {
        IReadOnlyList<WindowBounds> zones = Zones(ZoneTemplate.Columns, 3);

        // Una ventana a caballo, con mas superficie en la segunda columna.
        WindowBounds window = new(1100, 0, 400, 800);

        Assert.Equal(1, ZoneLayout.ZoneAt(zones, window));
    }

    [Fact]
    public void A_window_off_the_screen_is_in_no_zone()
    {
        Assert.Equal(-1, ZoneLayout.ZoneAt(Zones(ZoneTemplate.Columns, 3), new WindowBounds(-5000, 0, 100, 100)));
    }

    [Fact]
    public void The_zone_under_the_pointer_is_the_one_it_is_inside()
    {
        IReadOnlyList<WindowBounds> zones = Zones(ZoneTemplate.Columns, 4);

        Assert.Equal(0, ZoneLayout.ZoneUnder(zones, 10, 10));
        Assert.Equal(3, ZoneLayout.ZoneUnder(zones, 3400, 1300));
        Assert.Equal(-1, ZoneLayout.ZoneUnder(zones, 99999, 10));
    }

    private static bool Overlaps(WindowBounds a, WindowBounds b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}

public class ZoneSettingsTests
{
    [Fact]
    public void Asking_for_a_monitor_that_was_never_configured_creates_it_without_zones()
    {
        ZoneSettings settings = new();

        MonitorZoneSettings monitor = settings.For(2);

        Assert.Equal(2, monitor.Monitor);
        Assert.Equal(ZoneTemplate.None, monitor.Template);
        Assert.Single(settings.Monitors);
    }

    [Fact]
    public void Asking_twice_gives_the_same_one_back()
    {
        ZoneSettings settings = new();

        Assert.Same(settings.For(1), settings.For(1));
    }

    /// <summary>
    /// Dos entradas para la misma pantalla serían dos repartos distintos, y cuál gana dependería
    /// del orden del fichero.
    /// </summary>
    [Fact]
    public void A_duplicated_monitor_in_the_file_is_collapsed()
    {
        ZoneSettings settings = new()
        {
            Monitors =
            {
                new MonitorZoneSettings { Monitor = 1, Template = ZoneTemplate.Columns },
                new MonitorZoneSettings { Monitor = 1, Template = ZoneTemplate.Rows }
            }
        };

        settings.Normalise();

        MonitorZoneSettings only = Assert.Single(settings.Monitors);
        Assert.Equal(ZoneTemplate.Columns, only.Template);
    }

    [Fact]
    public void The_number_of_shortcuts_needed_is_the_busiest_monitor()
    {
        ZoneSettings settings = new()
        {
            Monitors =
            {
                new MonitorZoneSettings { Monitor = 1, Template = ZoneTemplate.Columns, Count = 3 },
                new MonitorZoneSettings { Monitor = 2, Template = ZoneTemplate.Grid, Count = 6 }
            }
        };

        Assert.Equal(6, settings.MaximumZoneCount);
    }

    [Fact]
    public void With_no_monitors_configured_no_shortcuts_are_needed()
    {
        Assert.Equal(0, new ZoneSettings().MaximumZoneCount);
    }
}
