using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Llevar una ventana al monitor que dice la regla.</summary>
public class MonitorActionTests
{
    private static (RuleApplicationService Service, FakeWindowManager Windows, FakeWindowEventService Events)
        Build(int monitors, params WindowRule[] rules)
    {
        FakeWindowManager windows = new();

        for (int number = 2; number <= monitors; number++)
        {
            windows.Monitors.Add(new MonitorInfo(
                number,
                new WindowBounds(-1920, 0, 1920, 1080),
                new WindowBounds(-1920, 0, 1920, 1040),
                false));
        }

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

    private static WindowRule MonitorRule(int monitor) => new()
    {
        Name = "A la otra pantalla",
        Priority = 100,
        Match = new WindowMatcher { ProcessName = "notepad.exe" },
        Actions = new WindowActions { Monitor = monitor }
    };

    [Fact]
    public void The_window_is_moved_to_the_monitor_the_rule_says()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(monitors: 2, MonitorRule(2));

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);
        events.RaiseShown(window);

        Assert.Equal((5, 2), windows.MonitorMoves.Single());

        service.Dispose();
    }

    /// <summary>
    /// Con un solo monitor no hay a dónde mover, y antes esto contestaba "no soportado" en cada
    /// evaluación, llenando el log de ruido sobre algo que el usuario no puede arreglar.
    /// </summary>
    [Fact]
    public void With_a_single_monitor_nothing_is_attempted()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(monitors: 1, MonitorRule(2));

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);
        events.RaiseShown(window);

        Assert.Empty(windows.MonitorMoves);

        service.Dispose();
    }

    [Fact]
    public void A_rule_without_a_monitor_moves_nothing()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(monitors: 2, new WindowRule
            {
                Name = "Solo escritorio",
                Priority = 100,
                Match = new WindowMatcher { ProcessName = "notepad.exe" },
                Actions = new WindowActions { Desktop = "2" }
            });

        WindowInfo window = TestWindows.Create(handle: 5);
        windows.Add(window);
        events.RaiseShown(window);

        Assert.Empty(windows.MonitorMoves);

        service.Dispose();
    }

    [Fact]
    public void The_monitor_survives_a_round_trip_through_the_rule_engine()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { MonitorRule(2) });

        RuleEvaluationResult result = engine.Evaluate(TestWindows.Create(handle: 1));

        Assert.Equal(2, result.Actions.Monitor);
    }
}
