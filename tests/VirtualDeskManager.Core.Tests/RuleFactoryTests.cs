using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class RuleFactoryTests
{
    private static IReadOnlyList<VirtualDesktopInfo> Desktops() => new[]
    {
        new VirtualDesktopInfo(Guid.Parse("11111111-1111-1111-1111-111111111111"), 0, ""),
        new VirtualDesktopInfo(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, "Desarrollo"),
        new VirtualDesktopInfo(Guid.Parse("33333333-3333-3333-3333-333333333333"), 2, "")
    };

    // ------------------------------------------------------------------ resolver

    [Fact]
    public void A_desktop_is_found_by_the_name_the_user_gave_it()
    {
        Assert.Equal(1, DesktopResolver.Resolve("Desarrollo", Desktops())!.Index);
        Assert.Equal(1, DesktopResolver.Resolve("desarrollo", Desktops())!.Index);
    }

    [Fact]
    public void A_desktop_is_found_by_its_number_counting_from_one()
    {
        Assert.Equal(0, DesktopResolver.Resolve("1", Desktops())!.Index);
        Assert.Equal(2, DesktopResolver.Resolve("3", Desktops())!.Index);
    }

    [Fact]
    public void A_desktop_is_found_by_its_identifier()
    {
        Assert.Equal(2, DesktopResolver.Resolve("33333333-3333-3333-3333-333333333333", Desktops())!.Index);
    }

    [Fact]
    public void A_number_outside_the_range_finds_nothing()
    {
        Assert.Null(DesktopResolver.Resolve("9", Desktops()));
        Assert.Null(DesktopResolver.Resolve("0", Desktops()));
    }

    [Fact]
    public void An_unknown_name_finds_nothing_instead_of_guessing()
    {
        Assert.Null(DesktopResolver.Resolve("Comunicación", Desktops()));
        Assert.Null(DesktopResolver.Resolve("", Desktops()));
        Assert.Null(DesktopResolver.Resolve(null, Desktops()));
    }

    [Fact]
    public void A_desktop_is_written_by_name_when_it_has_one_and_by_number_otherwise()
    {
        Assert.Equal("Desarrollo", DesktopResolver.Describe(Desktops()[1]));
        Assert.Equal("1", DesktopResolver.Describe(Desktops()[0]));
    }

    [Fact]
    public void What_is_written_can_be_read_back()
    {
        foreach (VirtualDesktopInfo desktop in Desktops())
        {
            string written = DesktopResolver.Describe(desktop);
            Assert.Equal(desktop.Id, DesktopResolver.Resolve(written, Desktops())!.Id);
        }
    }

    // ------------------------------------------------------------------ factory

    [Fact]
    public void A_rule_made_from_a_window_matches_its_application()
    {
        WindowInfo window = TestWindows.Create(processName: "Spotify.exe", title: "Spotify Premium");

        WindowRule rule = RuleFactory.ForApplication(window, new WindowActions { Desktop = "Desarrollo" });

        Assert.Equal("Spotify", rule.Name);
        Assert.Equal("Spotify.exe", rule.Match.ProcessName);
        Assert.Equal("Desarrollo", rule.Actions.Desktop);
        Assert.True(rule.Enabled);
        Assert.Null(rule.Match.TitleContains);
    }

    [Fact]
    public void The_rule_of_an_application_is_found_again()
    {
        List<WindowRule> rules = new()
        {
            new WindowRule { Name = "Spotify", Match = new WindowMatcher { ProcessName = "Spotify.exe" } }
        };

        Assert.NotNull(RuleFactory.FindForApplication(rules, "spotify.exe"));
        Assert.NotNull(RuleFactory.FindForApplication(rules, "Spotify"));
        Assert.Null(RuleFactory.FindForApplication(rules, "notepad.exe"));
    }

    [Fact]
    public void A_more_specific_rule_of_the_user_is_left_alone()
    {
        // It also looks at the title, so it is not "the rule for this application".
        List<WindowRule> rules = new()
        {
            new WindowRule
            {
                Name = "Spotify only when playing",
                Match = new WindowMatcher { ProcessName = "Spotify.exe", TitleContains = "Playing" }
            }
        };

        Assert.Null(RuleFactory.FindForApplication(rules, "Spotify.exe"));
    }

    [Fact]
    public void Merging_adds_without_erasing_what_was_not_mentioned()
    {
        WindowActions existing = new() { AlwaysOnTop = true, Desktop = "1" };

        RuleFactory.Merge(existing, new WindowActions { Desktop = "Desarrollo" });

        Assert.Equal("Desarrollo", existing.Desktop);
        Assert.True(existing.AlwaysOnTop);
    }

    [Fact]
    public void Merging_can_turn_an_action_off()
    {
        WindowActions existing = new() { AlwaysOnTop = true };

        RuleFactory.Merge(existing, new WindowActions { AlwaysOnTop = false });

        Assert.False(existing.AlwaysOnTop);
    }
}
