using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>The rule actions that needed the advanced backend: desktop and pinning.</summary>
public class RuleDesktopActionTests
{
    private const nint Window = 5;

    private static (RuleApplicationService Service, FakeWindowManager Windows, FakeVirtualDesktopService Desktops, FakeWindowEventService Events)
        Build(WindowActions actions, bool advanced = true, string process = "notepad.exe")
    {
        FakeWindowManager windows = new();
        WindowInfo window = TestWindows.Create(handle: Window, processName: process);
        windows.Add(window);

        FakeVirtualDesktopService desktops = FakeVirtualDesktopService.WithDesktops(3);

        if (!advanced)
        {
            desktops.Capabilities = VirtualDesktopCapabilities.None;
        }

        desktops.WindowDesktops[Window] = desktops.Desktops[0].Id;

        FakeWindowEventService events = new();
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            new WindowRule
            {
                Name = "test",
                Match = new WindowMatcher { ProcessName = process },
                Actions = actions
            }
        });

        RuleApplicationService service = new(engine, windows, desktops, events, ownProcessId: 999);
        service.Start();

        return (service, windows, desktops, events);
    }

    [Fact]
    public void A_rule_sends_the_window_to_the_desktop_it_names_by_number()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "2" });

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Equal(desktops.Desktops[1].Id, desktops.Moves.Single().Desktop);
        service.Dispose();
    }

    [Fact]
    public void A_rule_sends_the_window_to_the_desktop_it_names_by_name()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "Main" });

        events.RaiseShown(TestWindows.Create(handle: Window));

        // "Main" is the name the fake gives the first desktop, where the window already is.
        Assert.Empty(desktops.Moves);
        service.Dispose();
    }

    [Fact]
    public void A_window_already_on_the_right_desktop_is_not_moved_again()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "1" });

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Empty(desktops.Moves);
        service.Dispose();
    }

    [Fact]
    public void Applying_a_rule_never_drags_the_user_to_another_desktop()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "3" });

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Single(desktops.Moves);
        Assert.Empty(desktops.Switches);
        service.Dispose();
    }

    [Fact]
    public void An_unknown_desktop_does_not_move_the_window_anywhere()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "Comunicación" });

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Empty(desktops.Moves);
        service.Dispose();
    }

    [Fact]
    public void A_rule_can_show_a_window_on_every_desktop()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { PinWindow = true });

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.True(desktops.Pinned[Window]);
        service.Dispose();
    }

    // The window is shown on every desktop and a rule also says which desktop it opens on.
    // The two contradict each other, and Windows resolves the contradiction the worst way: the
    // move answers success and quietly cancels the pin.
    [Fact]
    public void A_rule_does_not_take_a_window_off_every_desktop_by_moving_it()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "2" });

        desktops.Pinned[Window] = true;

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Empty(desktops.Moves);
        Assert.True(desktops.Pinned[Window]);
        service.Dispose();
    }

    // The rules run again on every focus change, so a move that undoes the pin would undo it
    // over and over, not just once.
    [Fact]
    public void The_window_still_shows_everywhere_after_being_focused_again()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "2" });

        desktops.Pinned[Window] = true;

        events.RaiseShown(TestWindows.Create(handle: Window));
        events.RaiseForeground(TestWindows.Create(handle: Window));

        Assert.True(desktops.Pinned[Window]);
        service.Dispose();
    }

    // A rule that asks for both gets both, in the only order that survives: unpin, move, pin.
    [Fact]
    public void A_rule_asking_for_a_desktop_and_for_every_desktop_ends_pinned()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "2", PinWindow = true });

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Equal(desktops.Desktops[1].Id, desktops.Moves.Single().Desktop);
        Assert.True(desktops.Pinned[Window]);
        service.Dispose();
    }

    // Asking to stop showing it everywhere is how a rule says it really wants one desktop.
    [Fact]
    public void A_rule_that_unpins_the_window_can_then_move_it()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "2", PinWindow = false });

        desktops.Pinned[Window] = true;

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Equal(desktops.Desktops[1].Id, desktops.Moves.Single().Desktop);
        Assert.False(desktops.Pinned[Window]);
        service.Dispose();
    }

    [Fact]
    public void Without_the_advanced_backend_nothing_happens_and_nothing_throws()
    {
        (RuleApplicationService service, _, FakeVirtualDesktopService desktops, FakeWindowEventService events) =
            Build(new WindowActions { Desktop = "2", PinWindow = true }, advanced: false);

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Empty(desktops.Moves);
        Assert.Empty(desktops.Pinned);
        service.Dispose();
    }
}
