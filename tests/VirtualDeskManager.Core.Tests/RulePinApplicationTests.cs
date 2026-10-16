using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// Anclar la aplicación entera, que no es lo mismo que anclar sus ventanas.
/// </summary>
/// <remarks>
/// Esto sale de un fallo con Teams: estaba puesto para verse en todos los escritorios, y al abrir
/// una videollamada la ventana nueva nacía sin heredarlo. Anclar ventanas alcanza sólo a las que
/// existen en ese momento; Windows se acuerda de una <b>aplicación</b> anclada y se lo aplica a
/// las que abra después.
///
/// La acción existía en las reglas y en la interfaz desde el principio, pero contestaba siempre
/// que el backend no sabía hacerlo. Faltaba saber el identificador de aplicación de una ventana.
/// </remarks>
public class RulePinApplicationTests
{
    private const string Teams = "MSTeams_8wekyb3d8bbwe!MSTeams";

    private static WindowRule PinTeams() => new()
    {
        Name = "ms-teams",
        Priority = 100,
        Match = new WindowMatcher { ProcessName = "ms-teams.exe" },
        Actions = new WindowActions { PinApplication = true }
    };

    private static WindowInfo TeamsWindow(nint handle) =>
        TestWindows.Create(
            handle: handle,
            processName: "ms-teams.exe",
            title: "Microsoft Teams",
            className: "TeamsWebView",
            executablePath: @"C:\Program Files\WindowsApps\MSTeams\ms-teams.exe",
            processId: 4242);

    private static (RuleApplicationService Service, FakeVirtualDesktopService Desktops,
        FakeWindowManager Windows, FakeWindowEventService Events) Build()
    {
        FakeWindowManager windows = new();
        FakeWindowEventService events = new();
        FakeVirtualDesktopService desktops = new()
        {
            Capabilities = FakeVirtualDesktopService.FullCapabilities
        };

        RuleEngine engine = new();
        engine.SetRules(new[] { PinTeams() });

        RuleApplicationService service = new(engine, windows, desktops, events, ownProcessId: 999);
        service.Start();

        return (service, desktops, windows, events);
    }

    [Fact]
    public void A_rule_can_pin_the_whole_application()
    {
        (RuleApplicationService service, FakeVirtualDesktopService desktops,
            FakeWindowManager windows, FakeWindowEventService events) = Build();

        WindowInfo main = TeamsWindow(10);
        desktops.Applications[main.Handle] = Teams;

        windows.Add(main);
        events.RaiseShown(main);

        Assert.True(desktops.PinnedApplications[Teams]);

        service.Dispose();
    }

    /// <summary>
    /// El fallo tal cual se vio: la videollamada es una ventana nueva, y antes nacía sin heredar
    /// nada porque lo anclado era la ventana anterior y no la aplicación.
    /// </summary>
    [Fact]
    public void A_call_window_opened_later_is_already_covered()
    {
        (RuleApplicationService service, FakeVirtualDesktopService desktops,
            FakeWindowManager windows, FakeWindowEventService events) = Build();

        WindowInfo main = TeamsWindow(10);
        desktops.Applications[main.Handle] = Teams;
        windows.Add(main);
        events.RaiseShown(main);

        // La videollamada: otra ventana, de la misma aplicacion, abierta despues.
        WindowInfo call = TeamsWindow(11);
        desktops.Applications[call.Handle] = Teams;

        Assert.True(desktops.IsApplicationPinned(call.Handle));

        service.Dispose();
    }

    /// <summary>Anclar la aplicación no es anclar una ventana suya: son dos estados distintos.</summary>
    [Fact]
    public void Pinning_the_application_does_not_pin_the_window_by_itself()
    {
        (RuleApplicationService service, FakeVirtualDesktopService desktops,
            FakeWindowManager windows, FakeWindowEventService events) = Build();

        WindowInfo main = TeamsWindow(10);
        desktops.Applications[main.Handle] = Teams;

        windows.Add(main);
        events.RaiseShown(main);

        Assert.False(desktops.IsWindowPinned(main.Handle));

        service.Dispose();
    }

    /// <summary>Si ya está como la regla pide, no se vuelve a tocar.</summary>
    [Fact]
    public void An_application_that_is_already_pinned_is_left_alone()
    {
        (RuleApplicationService service, FakeVirtualDesktopService desktops,
            FakeWindowManager windows, FakeWindowEventService events) = Build();

        WindowInfo main = TeamsWindow(10);
        desktops.Applications[main.Handle] = Teams;
        desktops.PinnedApplications[Teams] = true;

        windows.Add(main);
        events.RaiseShown(main);

        Assert.True(desktops.PinnedApplications[Teams]);

        service.Dispose();
    }

    /// <summary>
    /// Sin saber de qué aplicación es la ventana no se ancla nada.
    /// </summary>
    /// <remarks>
    /// Es lo correcto y no una carencia: el identificador es de la aplicación, así que fallar al
    /// resolverlo y anclar de todas formas anclaría a otra.
    /// </remarks>
    [Fact]
    public void Without_an_application_id_nothing_is_pinned()
    {
        (RuleApplicationService service, FakeVirtualDesktopService desktops,
            FakeWindowManager windows, FakeWindowEventService events) = Build();

        WindowInfo main = TeamsWindow(10);

        windows.Add(main);
        events.RaiseShown(main);

        Assert.Empty(desktops.PinnedApplications);

        service.Dispose();
    }

    /// <summary>
    /// Una regla con escritorio y anclaje a la vez no se pelea consigo misma al reaplicarse.
    /// </summary>
    /// <remarks>
    /// Windows contesta 0x8007139F --estado no valido-- al mandar a un escritorio algo anclado, y
    /// eso dejaba un aviso de fallo en el diario cada vez, avisando de algo que ya estaba como se
    /// pedia. Se vio en el diario del usuario con su regla de Code.
    /// </remarks>
    [Fact]
    public void An_already_pinned_application_is_not_dragged_to_a_desktop()
    {
        FakeWindowManager windows = new();
        FakeWindowEventService events = new();
        FakeVirtualDesktopService desktops = new()
        {
            Capabilities = FakeVirtualDesktopService.FullCapabilities
        };

        Guid cuatro = Guid.NewGuid();
        desktops.Desktops.Add(new VirtualDesktopInfo(cuatro, 3, "Cuatro"));

        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            new WindowRule
            {
                Name = "ms-teams",
                Priority = 100,
                Match = new WindowMatcher { ProcessName = "ms-teams.exe" },
                Actions = new WindowActions { PinApplication = true, Desktop = "4" }
            }
        });

        RuleApplicationService service = new(engine, windows, desktops, events, ownProcessId: 999);
        service.Start();

        WindowInfo main = TeamsWindow(10);
        desktops.Applications[main.Handle] = Teams;
        desktops.PinnedApplications[Teams] = true;

        windows.Add(main);
        events.RaiseShown(main);

        Assert.Empty(desktops.Moves);

        service.Dispose();
    }

    [Fact]
    public void A_backend_that_cannot_pin_applications_says_so_instead_of_pretending()
    {
        FakeWindowManager windows = new();
        FakeWindowEventService events = new();
        FakeVirtualDesktopService desktops = new()
        {
            Capabilities = new VirtualDesktopCapabilities { CanPinWindow = true }
        };

        RuleEngine engine = new();
        engine.SetRules(new[] { PinTeams() });

        RuleApplicationService service = new(engine, windows, desktops, events, ownProcessId: 999);
        service.Start();

        WindowInfo main = TeamsWindow(10);
        desktops.Applications[main.Handle] = Teams;

        windows.Add(main);
        events.RaiseShown(main);

        Assert.Empty(desktops.PinnedApplications);
        Assert.Null(desktops.IsApplicationPinned(main.Handle));

        service.Dispose();
    }
}
