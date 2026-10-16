using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// "Go to the window this application already has instead of keeping the new one."
/// </summary>
/// <remarks>
/// The action that closes one of the user's windows, so what it must <em>not</em> do matters more
/// than what it does. Most of these tests are about the cases where it has to keep its hands off.
/// </remarks>
public class ReuseExistingWindowTests
{
    private static (RuleApplicationService Service, FakeWindowManager Windows, FakeWindowEventService Events)
        Build(params WindowRule[] rules) => BuildWith(new FakeVirtualDesktopService(), rules);

    private static (RuleApplicationService Service, FakeWindowManager Windows, FakeWindowEventService Events)
        BuildWith(FakeVirtualDesktopService desktops, params WindowRule[] rules)
    {
        FakeWindowManager windows = new();
        FakeWindowEventService events = new();
        RuleEngine engine = new();
        engine.SetRules(rules);

        RuleApplicationService service = new(engine, windows, desktops, events, ownProcessId: 999);

        service.Start();
        return (service, windows, events);
    }

    /// <summary>Dos escritorios, mirando el primero y con la aplicación viviendo en el segundo.</summary>
    private static FakeVirtualDesktopService TwoDesktops(nint existingWindow)
    {
        FakeVirtualDesktopService desktops = FakeVirtualDesktopService.WithDesktops(2);

        desktops.CurrentDesktop = desktops.Desktops[0].Id;
        desktops.WindowDesktops[existingWindow] = desktops.Desktops[1].Id;

        return desktops;
    }

    private static WindowRule ReuseRule(string process = "olk.exe") => new()
    {
        Name = "Outlook, one window",
        Priority = 100,
        Match = new WindowMatcher { ProcessName = process },
        Actions = new WindowActions { ReuseExistingWindow = true }
    };

    private static WindowInfo Window(nint handle, string process = "olk.exe", uint processId = 1234) =>
        TestWindows.Create(handle: handle, processName: process, processId: processId);

    /// <summary>Una regla que solo mueve de escritorio, sin decir nada de instancias.</summary>
    /// <remarks>
    /// Es el caso de serie, el que trae a casi todo el mundo aqui: la regla no opina sobre
    /// duplicados, asi que se pregunta.
    /// </remarks>
    private static WindowRule PlainRule(string process = "olk.exe") => new()
    {
        Name = "Outlook al 2",
        Priority = 100,
        Match = new WindowMatcher { ProcessName = process },
        Actions = new WindowActions { Desktop = "2" }
    };

    /// <summary>
    /// La recién llegada se va a donde vive su aplicación, y el usuario se queda donde estaba
    /// hasta que decida.
    /// </summary>
    [Fact]
    public void The_new_window_goes_to_where_the_application_already_lives()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, PlainRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Contains((2, desktops.Desktops[1].Id), desktops.Moves);

        service.Dispose();
    }

    /// <summary>
    /// Lo que más importa de todo esto: no se cierra nada. Cerrar la ventana recién abierta se
    /// llevaría por delante la página o la notificación que el usuario acaba de pinchar.
    /// </summary>
    [Fact]
    public void By_default_nothing_is_closed()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, PlainRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    /// <summary>Y no se arrastra al usuario: se le pregunta.</summary>
    [Fact]
    public void The_user_is_asked_instead_of_being_dragged_there()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, PlainRule());

        WindowMovedEventArgs? announced = null;
        service.WindowMovedToAnotherDesktop += (_, e) => announced = e;

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(desktops.Switches);
        Assert.NotNull(announced);
        Assert.Equal(desktops.Desktops[1].Id, announced!.Desktop.Id);

        service.Dispose();
    }

    /// <summary>Si la aplicación ya vive donde estamos, no hay nada que mover ni que preguntar.</summary>
    [Fact]
    public void Nothing_happens_when_the_application_already_lives_here()
    {
        FakeVirtualDesktopService desktops = FakeVirtualDesktopService.WithDesktops(2);
        desktops.CurrentDesktop = desktops.Desktops[0].Id;
        desktops.WindowDesktops[1] = desktops.Desktops[0].Id;

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        bool asked = false;
        service.WindowMovedToAnotherDesktop += (_, _) => asked = true;

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(desktops.Moves);
        Assert.False(asked);

        service.Dispose();
    }

    /// <summary>The first window of an application is not a duplicate of anything.</summary>
    [Fact]
    public void The_only_window_of_an_application_is_left_alone()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(ReuseRule());

        WindowInfo only = Window(1);
        windows.Add(only);
        events.RaiseShown(only);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    /// <summary>
    /// The one that would hurt most. Alt-tabbing to the second window of an application raises a
    /// foreground change, and acting on that would close the window the user just switched to.
    /// </summary>
    [Fact]
    public void Switching_to_an_existing_window_never_closes_anything()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(ReuseRule());

        WindowInfo first = Window(1);
        WindowInfo second = Window(2);

        windows.Add(first);
        windows.Add(second);

        events.RaiseForeground(second);
        events.RaiseShown(second);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    /// <summary>
    /// At startup every window looks new. Closing half of them because the user had two of
    /// something would be a spectacular way to lose their work.
    /// </summary>
    [Fact]
    public void Applying_to_windows_that_were_already_open_closes_nothing()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService _) =
            Build(ReuseRule());

        windows.Add(Window(1));
        windows.Add(Window(2));
        windows.Add(Window(3));

        service.ApplyToExistingWindows();

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    [Fact]
    public void An_application_without_the_action_keeps_all_its_windows()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(new WindowRule
            {
                Name = "Code",
                Priority = 100,
                Match = new WindowMatcher { ProcessName = "Code.exe" },
                Actions = new WindowActions { Desktop = "2" }
            });

        WindowInfo first = Window(1, "Code.exe");
        WindowInfo second = Window(2, "Code.exe");

        windows.Add(first);
        windows.Add(second);
        events.RaiseShown(second);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    [Fact]
    public void A_window_of_a_different_application_is_not_treated_as_a_duplicate()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(ReuseRule());

        windows.Add(Window(1, "Code.exe"));

        WindowInfo outlook = Window(2);
        windows.Add(outlook);
        events.RaiseShown(outlook);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    /// <summary>
    /// An application that answers a second launch with a whole new process is still the same
    /// program as far as the user is concerned.
    /// </summary>
    [Fact]
    public void A_duplicate_in_another_process_still_counts()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, PlainRule());

        windows.Add(Window(1, processId: 100));

        WindowInfo duplicate = Window(2, processId: 200);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Contains((2, desktops.Desktops[1].Id), desktops.Moves);

        service.Dispose();
    }

    [Fact]
    public void The_rules_being_paused_stops_it_like_everything_else()
    {
        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            Build(ReuseRule());

        service.Paused = true;

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    [Fact]
    public void The_action_survives_a_round_trip_through_the_rule_engine()
    {
        RuleEngine engine = new();
        engine.SetRules(new[] { ReuseRule() });

        RuleEvaluationResult result = engine.Evaluate(Window(1));

        Assert.True(result.Actions.ReuseExistingWindow);
        Assert.Equal("Outlook, one window", result.Actions.Sources[nameof(ResolvedWindowActions.ReuseExistingWindow)]);
    }

    [Fact]
    public void Turning_it_off_in_a_rule_is_not_the_same_as_leaving_it_unset()
    {
        WindowActions off = new() { ReuseExistingWindow = false };
        WindowActions unset = new();

        Assert.True(off.HasAny);
        Assert.False(unset.HasAny);
    }

    // ------------------------------------------------------------------ las tres respuestas

    /// <summary>
    /// Con "ir" puesto en la regla no se pregunta: se va y punto.
    /// </summary>
    /// <remarks>
    /// El usuario ya contestó, y al ponerlo en la regla contestó para todas las veces
    /// siguientes. Preguntar otra vez sería no haberle hecho caso.
    /// </remarks>
    [Fact]
    public void With_go_set_in_the_rule_it_goes_without_asking()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        WindowMovedEventArgs? announced = null;
        service.WindowMovedToAnotherDesktop += (_, e) => announced = e;

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Equal(desktops.Desktops[1].Id, Assert.Single(desktops.Switches));
        Assert.Null(announced);

        service.Dispose();
    }

    /// <summary>
    /// Con "abrir nueva instancia" puesto, la ventana nueva se queda donde nació.
    /// </summary>
    /// <remarks>
    /// Dos ventanas del mismo programa, una en cada escritorio, es una manera legítima de
    /// trabajar. Por eso la opción existe en vez de darse el caso por imposible.
    /// </remarks>
    [Fact]
    public void With_a_new_instance_allowed_nothing_is_reused()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        WindowRule rule = new()
        {
            Name = "Outlook, varias",
            Priority = 100,
            Match = new WindowMatcher { ProcessName = "olk.exe" },
            Actions = new WindowActions { NewInstance = true }
        };

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, rule);

        WindowMovedEventArgs? announced = null;
        service.WindowMovedToAnotherDesktop += (_, e) => announced = e;

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(desktops.Switches);
        Assert.Null(announced);
        Assert.DoesNotContain(desktops.Moves, m => m.Handle == duplicate.Handle);

        service.Dispose();
    }

    /// <summary>Abrir nueva instancia gana sobre ir: lo contrario dejaría la opción sin efecto.</summary>
    [Fact]
    public void Allowing_a_new_instance_wins_over_going_to_the_existing_one()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        WindowRule rule = new()
        {
            Name = "Outlook, las dos cosas",
            Priority = 100,
            Match = new WindowMatcher { ProcessName = "olk.exe" },
            Actions = new WindowActions { ReuseExistingWindow = true, NewInstance = true }
        };

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, rule);

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(desktops.Switches);

        service.Dispose();
    }

    /// <summary>La opción nueva cuenta como acción, o una regla que sólo la lleve no haría nada.</summary>
    [Fact]
    public void Allowing_a_new_instance_is_an_action_of_its_own()
    {
        Assert.True(new WindowActions { NewInstance = true }.HasAny);
        Assert.True(new WindowActions { NewInstance = false }.HasAny);
        Assert.False(new WindowActions().HasAny);
    }

    // ------------------------------------------------------------------ ya estamos alli

    /// <summary>
    /// Windows te lleva al escritorio de la aplicación al activar su icono, y la ventana nueva
    /// nace ya allí. Es el caso que se reportó.
    /// </summary>
    /// <remarks>
    /// Antes se salía por aquí dando por hecho que no había nada que hacer, porque no había
    /// adónde mover ni a dónde ofrecer ir. Pero quedaban dos ventanas donde el usuario esperaba
    /// una, y nadie preguntaba nada.
    /// </remarks>
    private static FakeVirtualDesktopService SameDesktop(nint existingWindow)
    {
        FakeVirtualDesktopService desktops = FakeVirtualDesktopService.WithDesktops(2);

        desktops.CurrentDesktop = desktops.Desktops[1].Id;
        desktops.WindowDesktops[existingWindow] = desktops.Desktops[1].Id;

        return desktops;
    }

    [Fact]
    public void On_the_same_desktop_with_go_set_the_duplicate_is_closed()
    {
        FakeVirtualDesktopService desktops = SameDesktop(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Contains(duplicate.Handle, windows.Closed);
        Assert.DoesNotContain(1, windows.Closed);

        service.Dispose();
    }

    /// <summary>
    /// Sin decirlo en la regla no se cierra nada.
    /// </summary>
    /// <remarks>
    /// Cerrar una ventana que el usuario acaba de abrir es destructivo y silencioso, y aquí no
    /// hay pregunta que valga: "ir o quedarse" no significa nada cuando ir ya se ha ido.
    /// </remarks>
    [Fact]
    public void On_the_same_desktop_nothing_is_closed_unless_the_rule_says_so()
    {
        FakeVirtualDesktopService desktops = SameDesktop(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, PlainRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    // ------------------------------------------------------------------ cual es nueva de verdad

    /// <summary>
    /// Una ventana que llega como "mostrada" y no como "creada" sigue siendo nueva.
    /// </summary>
    /// <remarks>
    /// Esta es la prueba que faltaba, y su ausencia dejó la función muerta durante meses: todas
    /// las demás llamaban a RaiseCreated, que emite justo el aviso que Windows casi nunca entrega.
    /// Desde el 2026-10-09 todas usan RaiseShown, y la de abajo comprueba que «creada» también vale.
    /// Se midió en un equipo real con el diario en Trace: Paint y la segunda ventana del
    /// Explorador llegaron <b>sólo</b> como mostradas, nunca como creadas, porque al crearse una
    /// ventana todavía no tiene título ni es visible y el filtro de ventanas manejables la
    /// descarta.
    /// </remarks>
    [Fact]
    public void A_window_that_only_ever_arrives_as_shown_is_still_new()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Equal(desktops.Desktops[1].Id, Assert.Single(desktops.Switches));

        service.Dispose();
    }

    /// <summary>
    /// Y al revés: la que sí llega como «creada» (pasa con algunas, ya con título al crearse)
    /// también es nueva.
    /// </summary>
    [Fact]
    public void A_window_that_arrives_as_created_is_also_new()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseCreated(duplicate);

        Assert.Equal(desktops.Desktops[1].Id, Assert.Single(desktops.Switches));

        service.Dispose();
    }

    /// <summary>
    /// La segunda vez que se ve la misma ventana ya no es nueva.
    /// </summary>
    /// <remarks>
    /// Es lo que protege de cerrarle una ventana al usuario cada vez que alterna con Alt+Tab, que
    /// era el motivo por el que esto se ató a la creación en su día.
    /// </remarks>
    [Fact]
    public void Seeing_the_same_window_again_does_not_make_it_new()
    {
        FakeVirtualDesktopService desktops = SameDesktop(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);

        events.RaiseShown(duplicate);
        windows.Closed.Clear();

        // Y ahora otra vez, como cuando se vuelve a ella.
        events.RaiseShown(duplicate);
        events.RaiseForegroundChanged(duplicate);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    /// <summary>Ponerse delante no convierte en nueva a una ventana que nunca habíamos visto.</summary>
    /// <remarks>
    /// Puede pasar con una ventana que ya existía antes de arrancar: no se la ha visto nacer, y
    /// tomarla por recién abierta sería cerrarle al usuario algo que llevaba abierto.
    /// </remarks>
    [Fact]
    public void Coming_to_the_front_never_counts_as_being_born()
    {
        FakeVirtualDesktopService desktops = SameDesktop(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        windows.Add(Window(1));

        WindowInfo otra = Window(2);
        windows.Add(otra);
        events.RaiseForegroundChanged(otra);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }

    /// <summary>
    /// Con "ir" puesto, se acaba con UNA ventana, nazca donde nazca la nueva.
    /// </summary>
    /// <remarks>
    /// Antes no: si la recién abierta nacía en otro escritorio se la mandaba junto a la que ya
    /// estaba y ahí se quedaba, así que el usuario terminaba con las dos. Se midió lanzando
    /// Outlook desde otro escritorio — dos ventanas antes, tres después.
    /// </remarks>
    [Fact]
    public void With_go_set_the_duplicate_is_closed_wherever_it_was_born()
    {
        FakeVirtualDesktopService desktops = TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, ReuseRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Equal(duplicate.Handle, Assert.Single(windows.Closed));
        Assert.Equal(desktops.Desktops[1].Id, Assert.Single(desktops.Switches));
        Assert.Contains(1, windows.Activated);

        service.Dispose();
    }

    /// <summary>
    /// Sin "ir" en la regla no se cierra nada nunca, esté donde esté.
    /// </summary>
    /// <remarks>
    /// Es la lección que dejó el comportamiento antiguo, cuando se cerraba siempre: se llevaba por
    /// delante la página o la notificación que el usuario acababa de pinchar. Cerrar sólo se hace
    /// ahora cuando se ha pedido explícitamente.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Without_go_nothing_is_ever_closed(bool sameDesktop)
    {
        FakeVirtualDesktopService desktops = sameDesktop
            ? SameDesktop(existingWindow: 1)
            : TwoDesktops(existingWindow: 1);

        (RuleApplicationService service, FakeWindowManager windows, FakeWindowEventService events) =
            BuildWith(desktops, PlainRule());

        windows.Add(Window(1));

        WindowInfo duplicate = Window(2);
        windows.Add(duplicate);
        events.RaiseShown(duplicate);

        Assert.Empty(windows.Closed);

        service.Dispose();
    }
}
