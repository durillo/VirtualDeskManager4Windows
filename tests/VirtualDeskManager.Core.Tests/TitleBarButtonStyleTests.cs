using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class RgbColourTests
{
    [Theory]
    [InlineData("#3B82F6", 0x3B, 0x82, 0xF6)]
    [InlineData("3B82F6", 0x3B, 0x82, 0xF6)]
    [InlineData("#3b82f6", 0x3B, 0x82, 0xF6)]
    [InlineData("  #3B82F6  ", 0x3B, 0x82, 0xF6)]
    public void The_forms_a_person_actually_types_are_all_read(string text, byte r, byte g, byte b)
    {
        Assert.True(RgbColour.TryParse(text, out RgbColour colour));
        Assert.Equal(new RgbColour(r, g, b), colour);
    }

    /// <summary>La forma corta repite cada dígito, como en CSS.</summary>
    [Theory]
    [InlineData("#ABC", 0xAA, 0xBB, 0xCC)]
    [InlineData("f0a", 0xFF, 0x00, 0xAA)]
    public void The_three_digit_form_expands_the_way_css_does(string text, byte r, byte g, byte b)
    {
        Assert.True(RgbColour.TryParse(text, out RgbColour colour));
        Assert.Equal(new RgbColour(r, g, b), colour);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("azul")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#12345678")]
    public void Anything_else_is_refused_instead_of_guessed_at(string? text)
    {
        Assert.False(RgbColour.TryParse(text, out _));
    }

    [Fact]
    public void A_refused_colour_falls_back_to_grey_rather_than_throwing()
    {
        Assert.Equal(RgbColour.Grey, RgbColour.Parse("ni idea"));
    }

    [Fact]
    public void It_writes_itself_back_in_the_form_it_reads()
    {
        Assert.Equal("#3B82F6", RgbColour.Parse("#3b82f6").Text);
    }

    /// <summary>
    /// El dibujo del botón se pinta oscuro o claro según esto, así que confundir claro con oscuro
    /// lo hace invisible.
    /// </summary>
    [Theory]
    [InlineData("#FFFFFF", true)]
    [InlineData("#F59E0B", true)]
    [InlineData("#22C55E", false)]
    [InlineData("#202020", false)]
    [InlineData("#3B82F6", false)]
    [InlineData("#808080", false)]
    public void Light_and_dark_are_told_apart_by_how_the_eye_sees_them(string text, bool light)
    {
        Assert.Equal(light, RgbColour.Parse(text).IsLight);
    }

    /// <summary>
    /// Promediando los tres canales, un amarillo y un azul marino salen igual de claros. Con la
    /// luminancia percibida no.
    /// </summary>
    [Fact]
    public void Yellow_is_light_and_navy_is_not_although_they_average_the_same()
    {
        RgbColour yellow = RgbColour.Parse("#FFFF00");
        RgbColour navy = RgbColour.Parse("#0000FF");

        Assert.True(yellow.IsLight);
        Assert.False(navy.IsLight);
    }
}

public class TitleBarButtonSettingsTests
{
    [Fact]
    public void The_defaults_are_the_discreet_grey_button_of_before()
    {
        TitleBarButtonSettings settings = new();

        Assert.Equal(100, settings.SizePercent);
        Assert.Equal(25, settings.OpacityPercent);
        Assert.Equal(RgbColour.Grey, settings.ParsedColour);
    }

    [Fact]
    public void An_absurd_size_is_brought_back_into_range()
    {
        TitleBarButtonSettings settings = new() { SizePercent = 5000 };
        settings.Normalise();
        Assert.Equal(200, settings.SizePercent);

        settings.SizePercent = -3;
        settings.Normalise();
        Assert.Equal(50, settings.SizePercent);
    }

    /// <summary>
    /// Un botón del todo transparente existiría, ocuparía sitio en la barra de título y no se
    /// vería: sería un botón invisible que se traga las pulsaciones.
    /// </summary>
    [Fact]
    public void The_button_can_never_be_made_completely_invisible()
    {
        TitleBarButtonSettings settings = new() { OpacityPercent = 0 };
        settings.Normalise();

        Assert.Equal(10, settings.OpacityPercent);
    }

    [Fact]
    public void A_colour_that_makes_no_sense_is_rewritten_so_the_user_sees_what_is_really_set()
    {
        TitleBarButtonSettings settings = new() { Colour = "verde lima" };
        settings.Normalise();

        Assert.Equal("#808080", settings.Colour);
    }

    [Fact]
    public void A_good_colour_is_kept_and_tidied_up()
    {
        TitleBarButtonSettings settings = new() { Colour = "3b82f6" };
        settings.Normalise();

        Assert.Equal("#3B82F6", settings.Colour);
    }

    [Fact]
    public void Hovering_is_always_visibly_stronger_than_resting()
    {
        foreach (int resting in new[] { 10, 25, 50, 80, 100 })
        {
            TitleBarButtonSettings settings = new() { OpacityPercent = resting };
            settings.Normalise();

            Assert.True(
                settings.HoverOpacityPercent > settings.OpacityPercent || settings.OpacityPercent == 100,
                $"con {resting}% en reposo, al pasar el ratón sale {settings.HoverOpacityPercent}%");

            Assert.InRange(settings.HoverOpacityPercent, 40, 100);
        }
    }
}

public class TitleBarButtonSizeTests
{
    /// <summary>Sin tocar nada, el tamaño es exactamente el de siempre.</summary>
    [Theory]
    [InlineData(23, 14)]
    [InlineData(32, 20)]
    [InlineData(40, 25)]
    public void The_natural_size_is_unchanged(int captionHeight, int expected)
    {
        Assert.Equal(expected, TitleBarLayout.ComputeButtonSize(captionHeight));
        Assert.Equal(expected, TitleBarLayout.ComputeButtonSize(captionHeight, 100));
    }

    [Fact]
    public void The_percentage_moves_the_natural_size()
    {
        int natural = TitleBarLayout.ComputeButtonSize(32);

        Assert.True(TitleBarLayout.ComputeButtonSize(32, 200) > natural);
        Assert.True(TitleBarLayout.ComputeButtonSize(32, 50) < natural);
    }

    /// <summary>
    /// Ni el ajuste más extremo puede dejar el botón inservible: uno de tres píxeles no se puede
    /// pulsar y uno de cien no cabe en ninguna barra de título.
    /// </summary>
    [Theory]
    [InlineData(20, 10000)]
    [InlineData(200, 10000)]
    [InlineData(20, -500)]
    public void No_setting_can_produce_a_useless_button(int captionHeight, int percent)
    {
        Assert.InRange(TitleBarLayout.ComputeButtonSize(captionHeight, percent), 10, 64);
    }

    /// <summary>
    /// El hueco que se le pide a la ventana tiene que crecer con el botón, o un botón grande se
    /// colocaría en un sitio donde no cabe y taparía los botones de la aplicación.
    /// </summary>
    [Fact]
    public void A_bigger_button_asks_the_window_for_a_bigger_gap()
    {
        Assert.True(
            TitleBarLayout.ComputeRequiredFreeWidth(32, 200) > TitleBarLayout.ComputeRequiredFreeWidth(32, 100));
    }

    [Fact]
    public void A_bigger_button_still_lands_inside_the_caption()
    {
        WindowBounds caption = new(0, 0, 900, 32);

        TitleBarAnchor? anchor = TitleBarLayout.Compute(
            handle: 1,
            caption,
            isTopMost: false,
            extraOffset: 0,
            captionButtonsWidth: 140,
            sizePercent: 200);

        Assert.NotNull(anchor);
        Assert.True(anchor!.Value.X >= caption.X);
        Assert.True(anchor.Value.X + anchor.Value.Size <= caption.Right);
        Assert.True(anchor.Value.Y >= caption.Y);
        Assert.True(anchor.Value.Y + anchor.Value.Size <= caption.Bottom);
    }
}
