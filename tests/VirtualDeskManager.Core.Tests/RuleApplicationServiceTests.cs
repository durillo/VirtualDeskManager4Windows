using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class RuleApplicationServiceTests
{
    private static (RuleApplicationService Service, FakeWindowManager Windows, FakeWindowEventService Events)
        Build(params WindowRule[] rules)
    {
        FakeWindowManager windows = new();
        FakeWindowEventService events = new();
        RuleEngine engine = new();
        engine.SetRules(rules);

        RuleApplicationService service = new(
            engine,
            windows,
            new FakeVirtualDesktopService(),
            events,
            ownProcessId: 999);

        service.Start();
        return (service, windows, events);
    }

    private static WindowRule AlwaysOnTopRule() => new()
    {
        Name = "Notepad always on top",
        Priority = 100,
        Match = new WindowMatcher { ProcessName = "notepad.exe" },
        Actions = new WindowActions { AlwaysOnTop = true }
    };

    [Fact]
    public void A_new_window_gets_the_action_of_the_matching_rule()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(AlwaysOnTopRule());

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);
        events.RaiseShown(window);

        Assert.True(windows.AlwaysOnTopCalls.Single().Enabled);

        service.Dispose();
    }

    [Fact]
    public void A_window_that_already_has_the_state_is_not_touched()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(AlwaysOnTopRule());

        WindowInfo window = TestWindows.Create(handle: 5, topMost: true);
        windows.Add(window);
        events.RaiseShown(window);

        Assert.Empty(windows.AlwaysOnTopCalls);

        service.Dispose();
    }

    [Fact]
    public void Repeated_events_for_the_same_window_are_debounced()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(AlwaysOnTopRule());

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);

        events.RaiseShown(window);
        events.RaiseShown(window);
        events.RaiseForeground(window);

        Assert.Single(windows.AlwaysOnTopCalls);

        service.Dispose();
    }

    [Fact]
    public void Our_own_windows_are_ignored()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(AlwaysOnTopRule());

        WindowInfo window = TestWindows.Create(handle: 5, processId: 999);
        windows.Add(window);
        events.RaiseShown(window);

        Assert.Empty(windows.AlwaysOnTopCalls);

        service.Dispose();
    }

    [Fact]
    public void Paused_rules_do_nothing()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(AlwaysOnTopRule());

        service.Paused = true;

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);
        events.RaiseShown(window);

        Assert.Empty(windows.AlwaysOnTopCalls);

        service.Dispose();
    }

    [Fact]
    public void An_unsupported_action_does_not_throw()
    {
        WindowRule rule = new()
        {
            Name = "Spotify everywhere",
            Match = new WindowMatcher { ProcessName = "notepad.exe" },
            Actions = new WindowActions { PinApplication = true }
        };

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) = Build(rule);

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);

        events.RaiseShown(window);

        Assert.Empty(windows.AlwaysOnTopCalls);

        service.Dispose();
    }

    [Fact]
    public void Existing_windows_can_be_processed_on_demand()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(AlwaysOnTopRule());

        windows.Add(TestWindows.Create(handle: 5));
        service.ApplyToExistingWindows();

        Assert.Single(windows.AlwaysOnTopCalls);

        service.Dispose();
    }

    /// <summary>
    /// El fallo que el usuario describió como "muy incómodo": una regla que dice "maximizada"
    /// volvía a maximizar la ventana cada vez que ésta recuperaba el foco, de modo que no había
    /// manera de dejarla pequeña. En una mañana llegó a hacerlo 89 veces sobre la misma
    /// aplicación.
    /// </summary>
    [Fact]
    public void A_rule_does_not_reimpose_itself_every_time_the_window_gets_the_focus()
    {
        WindowRule maximise = new()
        {
            Name = "Code",
            Priority = 100,
            Match = new WindowMatcher { ProcessName = "notepad.exe" },
            Actions = new WindowActions { Maximized = true }
        };

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(maximise);

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);

        events.RaiseShown(window);

        // El usuario la desmaximiza a mano y vuelve a pincharla unas cuantas veces.
        for (int i = 0; i < 5; i++)
        {
            events.RaiseForeground(window);
        }

        Assert.Single(windows.StateCalls);

        service.Dispose();
    }

    /// <summary>
    /// Y lo mismo con fijar en todos los escritorios, que es como se manifestó con Teams: el
    /// usuario la desfijaba y la regla se lo deshacía tres milisegundos después.
    /// </summary>
    [Fact]
    public void A_rule_does_not_undo_what_the_user_just_did_by_hand()
    {
        WindowRule unpin = new()
        {
            Name = "ms-teams",
            Priority = 100,
            Match = new WindowMatcher { ProcessName = "notepad.exe" },
            Actions = new WindowActions { PinWindow = false }
        };

        FakeWindowManager windows = new();
        FakeWindowEventService events = new();
        FakeVirtualDesktopService desktops = new() { Capabilities = FakeVirtualDesktopService.FullCapabilities };

        RuleEngine engine = new();
        engine.SetRules(new[] { unpin });

        RuleApplicationService service = new(engine, windows, desktops, events, ownProcessId: 999);
        service.Start();

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);

        events.RaiseShown(window);

        int afterFirst = desktops.Pinned.Count;

        for (int i = 0; i < 5; i++)
        {
            events.RaiseForeground(window);
        }

        Assert.Equal(afterFirst, desktops.Pinned.Count);

        service.Dispose();
    }
}
