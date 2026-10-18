using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Desktops;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Placement and slide of the console that drops from the top of the screen.</summary>
public class DropDownConsoleLayoutTests
{
    private static readonly LayoutRect UltraWide = new(0, 0, 3440, 1440);

    /// <summary>A second monitor to the right, to catch anything assuming the origin.</summary>
    private static readonly LayoutRect Secondary = new(3440, 200, 1920, 1080);

    private static DropDownConsoleSettings Settings(
        int width = 80,
        int height = 45,
        int opacity = 90,
        int milliseconds = 120) =>
        new()
        {
            WidthPercent = width,
            HeightPercent = height,
            OpacityPercent = opacity,
            AnimationMilliseconds = milliseconds
        };

    [Fact]
    public void The_size_follows_the_percentages_of_the_screen()
    {
        LayoutRect target = DropDownConsoleLayout.Target(UltraWide, Settings(width: 50, height: 25));

        Assert.Equal(1720, target.Width);
        Assert.Equal(360, target.Height);
    }

    [Fact]
    public void The_same_percentages_give_a_different_size_on_a_different_screen()
    {
        DropDownConsoleSettings settings = Settings(width: 50, height: 50);

        LayoutRect wide = DropDownConsoleLayout.Target(UltraWide, settings);
        LayoutRect small = DropDownConsoleLayout.Target(Secondary, settings);

        Assert.Equal(1720, wide.Width);
        Assert.Equal(960, small.Width);
    }

    [Fact]
    public void It_is_centred_horizontally_and_glued_to_the_top()
    {
        LayoutRect target = DropDownConsoleLayout.Target(UltraWide, Settings(width: 60));

        int left = target.Left - UltraWide.Left;
        int right = UltraWide.Right - target.Right;

        Assert.True(Math.Abs(left - right) <= 1, $"{left} a la izquierda, {right} a la derecha");
        Assert.Equal(UltraWide.Top, target.Top);
    }

    [Fact]
    public void It_respects_a_monitor_that_does_not_start_at_the_origin()
    {
        LayoutRect target = DropDownConsoleLayout.Target(Secondary, Settings(width: 100, height: 50));

        Assert.Equal(Secondary.Left, target.Left);
        Assert.Equal(Secondary.Top, target.Top);
        Assert.Equal(Secondary.Width, target.Width);
    }

    [Fact]
    public void At_full_width_and_height_it_covers_the_screen()
    {
        LayoutRect target = DropDownConsoleLayout.Target(UltraWide, Settings(width: 100, height: 100));

        Assert.Equal(UltraWide, target);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    [InlineData(1000)]
    public void Impossible_percentages_are_brought_back_into_range(int percent)
    {
        DropDownConsoleSettings settings = new() { WidthPercent = percent, HeightPercent = percent };
        settings.Normalise();

        LayoutRect target = DropDownConsoleLayout.Target(UltraWide, settings);

        Assert.True(target.Width is > 0 and <= 3440, $"ancho {target.Width}");
        Assert.True(target.Height is > 0 and <= 1440, $"alto {target.Height}");
    }

    [Fact]
    public void Closed_it_sits_entirely_above_the_screen()
    {
        LayoutRect closed = DropDownConsoleLayout.At(UltraWide, Settings(), 0);

        Assert.True(closed.Bottom <= UltraWide.Top, $"asoma hasta {closed.Bottom}");
    }

    [Fact]
    public void Open_it_sits_exactly_at_its_target()
    {
        DropDownConsoleSettings settings = Settings();

        Assert.Equal(
            DropDownConsoleLayout.Target(UltraWide, settings),
            DropDownConsoleLayout.At(UltraWide, settings, 1));
    }

    [Fact]
    public void Halfway_down_it_is_partly_on_screen()
    {
        LayoutRect half = DropDownConsoleLayout.At(UltraWide, Settings(), 0.5);

        Assert.True(half.Top < UltraWide.Top, "ya deberia estar bajando");
        Assert.True(half.Bottom > UltraWide.Top, "deberia asomar algo");
    }

    /// <summary>
    /// The terminal reflows its text whenever its window changes size, so the console has to keep
    /// the same height all the way down and only change where it is.
    /// </summary>
    [Fact]
    public void The_size_never_changes_during_the_slide()
    {
        DropDownConsoleSettings settings = Settings();
        LayoutRect target = DropDownConsoleLayout.Target(UltraWide, settings);

        for (double progress = 0; progress <= 1.0001; progress += 0.05)
        {
            LayoutRect at = DropDownConsoleLayout.At(UltraWide, settings, progress);

            Assert.Equal(target.Width, at.Width);
            Assert.Equal(target.Height, at.Height);
            Assert.Equal(target.Left, at.Left);
        }
    }

    [Fact]
    public void It_only_ever_moves_downwards_while_opening()
    {
        DropDownConsoleSettings settings = Settings();
        int previous = int.MinValue;

        for (double progress = 0; progress <= 1.0001; progress += 0.05)
        {
            int top = DropDownConsoleLayout.At(UltraWide, settings, progress).Top;

            Assert.True(top >= previous, $"ha retrocedido de {previous} a {top}");
            previous = top;
        }
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void A_nonsensical_progress_still_gives_a_drawable_rectangle(double progress)
    {
        LayoutRect at = DropDownConsoleLayout.At(UltraWide, Settings(), progress);

        Assert.True(at.Width > 0);
        Assert.True(at.Height > 0);
        Assert.True(at.Top <= UltraWide.Top);
    }

    [Fact]
    public void Progress_runs_forwards_when_opening_and_backwards_when_closing()
    {
        DropDownConsoleSettings settings = Settings(milliseconds: 100);

        Assert.Equal(0, DropDownConsoleLayout.Progress(TimeSpan.Zero, settings, opening: true));
        Assert.Equal(1, DropDownConsoleLayout.Progress(TimeSpan.FromMilliseconds(100), settings, opening: true));

        Assert.Equal(1, DropDownConsoleLayout.Progress(TimeSpan.Zero, settings, opening: false));
        Assert.Equal(0, DropDownConsoleLayout.Progress(TimeSpan.FromMilliseconds(100), settings, opening: false));
    }

    [Fact]
    public void Progress_does_not_overshoot_past_the_end()
    {
        DropDownConsoleSettings settings = Settings(milliseconds: 100);

        Assert.Equal(1, DropDownConsoleLayout.Progress(TimeSpan.FromSeconds(9), settings, opening: true));
        Assert.Equal(0, DropDownConsoleLayout.Progress(TimeSpan.FromSeconds(9), settings, opening: false));
    }

    /// <summary>Zero milliseconds is a real setting: no animation, straight there.</summary>
    [Fact]
    public void Without_animation_it_is_there_from_the_first_frame()
    {
        DropDownConsoleSettings settings = Settings(milliseconds: 0);

        Assert.Equal(1, DropDownConsoleLayout.Progress(TimeSpan.Zero, settings, opening: true));
        Assert.Equal(0, DropDownConsoleLayout.Progress(TimeSpan.Zero, settings, opening: false));
    }

    [Theory]
    [InlineData(100, 255)]
    [InlineData(50, 128)]
    [InlineData(20, 51)]
    public void Opacity_becomes_an_alpha_windows_understands(int percent, int expected)
    {
        DropDownConsoleSettings settings = new() { OpacityPercent = percent };

        Assert.Equal(expected, settings.Alpha);
    }

    [Fact]
    public void Opacity_never_reaches_invisible()
    {
        DropDownConsoleSettings settings = new() { OpacityPercent = 0 };
        settings.Normalise();

        Assert.True(settings.Alpha >= 51, "una consola que no se ve no se puede recuperar");
    }

    [Fact]
    public void La_pestana_del_pin_cuelga_debajo_de_la_consola_a_la_derecha()
    {
        LayoutRect console = DropDownConsoleLayout.Target(Secondary, Settings(width: 50, height: 50));

        LayoutRect tab = DropDownConsoleLayout.PinTab(console, Secondary, 28);

        Assert.Equal(console.Bottom, tab.Top);
        Assert.Equal(console.Right - 56, tab.Left);
        Assert.Equal(28, tab.Width);
        Assert.Equal(28, tab.Height);
    }

    [Fact]
    public void La_pestana_del_pin_sigue_a_la_consola_mientras_baja()
    {
        DropDownConsoleSettings settings = Settings();
        LayoutRect halfway = DropDownConsoleLayout.At(UltraWide, settings, 0.5);

        LayoutRect tab = DropDownConsoleLayout.PinTab(halfway, UltraWide, 28);

        Assert.Equal(halfway.Bottom, tab.Top);
    }

    [Fact]
    public void Con_la_consola_hasta_abajo_la_pestana_se_mete_dentro_en_vez_de_perderse()
    {
        LayoutRect console = DropDownConsoleLayout.Target(Secondary, Settings(height: 100));

        LayoutRect tab = DropDownConsoleLayout.PinTab(console, Secondary, 28);

        Assert.Equal(Secondary.Bottom - 28, tab.Top);
        Assert.True(Secondary.Contains(tab.Left, tab.Top));
    }

    [Fact]
    public void Las_tiras_de_tamano_van_por_fuera_de_la_consola_a_los_lados_y_abajo()
    {
        LayoutRect console = DropDownConsoleLayout.Target(Secondary, Settings(width: 50, height: 50));

        IReadOnlyDictionary<ConsoleResizeEdge, LayoutRect> grips = DropDownConsoleLayout.ResizeGrips(console, Secondary, 6);

        Assert.Equal(console.Left - 6, grips[ConsoleResizeEdge.Left].Left);
        Assert.Equal(console.Right, grips[ConsoleResizeEdge.Right].Left);
        Assert.Equal(console.Bottom, grips[ConsoleResizeEdge.Bottom].Top);
        Assert.Equal(console.Top, grips[ConsoleResizeEdge.Left].Top);
        Assert.Equal(5, grips.Count);
    }

    [Fact]
    public void Con_la_consola_a_pantalla_completa_las_tiras_se_meten_dentro()
    {
        LayoutRect console = DropDownConsoleLayout.Target(Secondary, Settings(width: 100, height: 100));

        IReadOnlyDictionary<ConsoleResizeEdge, LayoutRect> grips = DropDownConsoleLayout.ResizeGrips(console, Secondary, 6);

        foreach (LayoutRect grip in grips.Values)
        {
            Assert.True(Secondary.Contains(grip.Left, grip.Top), $"{grip} se sale de la pantalla");
            Assert.True(grip.Right <= Secondary.Right && grip.Bottom <= Secondary.Bottom, $"{grip} se sale de la pantalla");
        }
    }

    [Fact]
    public void Arrastrar_un_lado_cambia_el_ancho_el_doble_porque_la_consola_va_centrada()
    {
        LayoutRect screen = new(0, 0, 2000, 1000);
        LayoutRect start = DropDownConsoleLayout.Target(screen, Settings(width: 50, height: 40));

        Assert.Equal((60, 40), DropDownConsoleLayout.Resize(screen, start, ConsoleResizeEdge.Right, 100, 999));
        Assert.Equal((60, 40), DropDownConsoleLayout.Resize(screen, start, ConsoleResizeEdge.Left, -100, 0));
        Assert.Equal((40, 40), DropDownConsoleLayout.Resize(screen, start, ConsoleResizeEdge.Left, 100, 0));
    }

    [Fact]
    public void Arrastrar_abajo_cambia_el_alto_y_las_esquinas_los_dos()
    {
        LayoutRect screen = new(0, 0, 2000, 1000);
        LayoutRect start = DropDownConsoleLayout.Target(screen, Settings(width: 50, height: 40));

        Assert.Equal((50, 55), DropDownConsoleLayout.Resize(screen, start, ConsoleResizeEdge.Bottom, 300, 150));
        Assert.Equal((60, 30), DropDownConsoleLayout.Resize(screen, start, ConsoleResizeEdge.BottomRight, 100, -100));
    }

    [Fact]
    public void El_tamano_nunca_baja_del_10_ni_pasa_del_100()
    {
        LayoutRect screen = new(0, 0, 2000, 1000);
        LayoutRect start = DropDownConsoleLayout.Target(screen, Settings(width: 50, height: 40));

        Assert.Equal((10, 10), DropDownConsoleLayout.Resize(screen, start, ConsoleResizeEdge.BottomRight, -5000, -5000));
        Assert.Equal((100, 100), DropDownConsoleLayout.Resize(screen, start, ConsoleResizeEdge.BottomRight, 5000, 5000));
    }

    [Fact]
    public void En_una_consola_estrechisima_la_pestana_no_se_sale_por_la_izquierda()
    {
        LayoutRect console = new(100, 0, 30, 200);

        LayoutRect tab = DropDownConsoleLayout.PinTab(console, UltraWide, 28);

        Assert.Equal(100, tab.Left);
    }
}
