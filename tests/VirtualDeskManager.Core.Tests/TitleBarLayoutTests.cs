using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Placement of the floating caption button.</summary>
public class TitleBarLayoutTests
{
    private static readonly WindowBounds TypicalCaption = new(100, 50, 1200, 32);

    [Fact]
    public void The_button_sits_inside_the_caption()
    {
        TitleBarAnchor anchor = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false)!.Value;

        Assert.True(anchor.X > TypicalCaption.X, "it must not overlap the window icon");
        Assert.True(anchor.X + anchor.Size < TypicalCaption.Right, "it must stay inside the caption");
        Assert.True(anchor.Y >= TypicalCaption.Y);
        Assert.True(anchor.Y + anchor.Size <= TypicalCaption.Bottom);
    }

    [Fact]
    public void The_button_is_vertically_centred()
    {
        TitleBarAnchor anchor = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false)!.Value;

        int topGap = anchor.Y - TypicalCaption.Y;
        int bottomGap = TypicalCaption.Bottom - (anchor.Y + anchor.Size);

        Assert.True(Math.Abs(topGap - bottomGap) <= 1, $"top {topGap}, bottom {bottomGap}");
    }

    [Fact]
    public void The_button_leaves_room_for_minimize_maximize_and_close()
    {
        TitleBarAnchor anchor = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false)!.Value;

        int reserved = TypicalCaption.Right - (anchor.X + anchor.Size);

        // Measured on Windows 11: the three buttons take about 6.3 times the caption height.
        Assert.True(reserved >= (int)(6.3 * TypicalCaption.Height), $"only {reserved} pixels were reserved");
    }

    [Fact]
    public void The_real_button_width_wins_over_the_estimate()
    {
        // What the desktop manager reports for a standard Windows 11 caption of 23 pixels.
        WindowBounds caption = new(0, 0, 1200, 23);

        TitleBarAnchor measured = TitleBarLayout.Compute(7, caption, isTopMost: false, captionButtonsWidth: 146)!.Value;
        TitleBarAnchor estimated = TitleBarLayout.Compute(7, caption, isTopMost: false)!.Value;

        int reserved = caption.Right - (measured.X + measured.Size);

        Assert.True(reserved >= 146, $"the measured width must be respected, only {reserved} were reserved");

        // The estimate is deliberately close to reality, so that a window whose caption cannot be
        // measured does not end up with the button somewhere absurd.
        Assert.True(Math.Abs(measured.X - estimated.X) <= 5, $"measured {measured.X}, estimated {estimated.X}");
    }

    [Fact]
    public void A_window_with_wide_custom_buttons_pushes_ours_further_left()
    {
        TitleBarAnchor narrow = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false, captionButtonsWidth: 100)!.Value;
        TitleBarAnchor wide = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false, captionButtonsWidth: 300)!.Value;

        Assert.Equal(narrow.X - 200, wide.X);
    }

    [Fact]
    public void A_nonsense_measurement_falls_back_to_the_estimate()
    {
        TitleBarAnchor fromZero = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false, captionButtonsWidth: 0)!.Value;
        TitleBarAnchor estimated = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false)!.Value;

        Assert.Equal(estimated.X, fromZero.X);
    }

    [Fact]
    public void The_offset_pushes_the_button_further_left()
    {
        TitleBarAnchor plain = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false)!.Value;
        TitleBarAnchor shifted = TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false, extraOffset: 60)!.Value;

        Assert.Equal(plain.X - 60, shifted.X);
    }

    [Fact]
    public void The_topmost_state_travels_with_the_anchor()
    {
        Assert.True(TitleBarLayout.Compute(7, TypicalCaption, isTopMost: true)!.Value.IsTopMost);
        Assert.False(TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false)!.Value.IsTopMost);
    }

    [Theory]
    [InlineData(0, 0)]      // no caption at all
    [InlineData(1200, 4)]   // a sliver, e.g. a full screen window
    [InlineData(120, 32)]   // too narrow once the caption buttons are there
    public void A_window_without_a_usable_caption_gets_no_button(int width, int height)
    {
        Assert.Null(TitleBarLayout.Compute(7, new WindowBounds(0, 0, width, height), isTopMost: false));
    }

    [Fact]
    public void An_invalid_handle_gets_no_button()
    {
        Assert.Null(TitleBarLayout.Compute(0, TypicalCaption, isTopMost: false));
    }

    [Fact]
    public void An_excessive_offset_hides_the_button_instead_of_pushing_it_off_the_window()
    {
        Assert.Null(TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false, extraOffset: 5000));
    }

    [Fact]
    public void The_size_grows_with_the_caption_but_stays_sane()
    {
        int small = TitleBarLayout.Compute(7, new WindowBounds(0, 0, 800, 20), isTopMost: false)!.Value.Size;
        int large = TitleBarLayout.Compute(7, new WindowBounds(0, 0, 1600, 80), isTopMost: false)!.Value.Size;

        Assert.True(small >= 12);
        Assert.True(large <= 40);
        Assert.True(large > small);
    }

    [Fact]
    public void The_same_caption_always_gives_the_same_anchor()
    {
        // The tracker relies on this to skip publishing while a window is being dragged.
        Assert.Equal(
            TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false),
            TitleBarLayout.Compute(7, TypicalCaption, isTopMost: false));
    }
}
