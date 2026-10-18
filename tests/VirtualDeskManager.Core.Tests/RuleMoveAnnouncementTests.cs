using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// Cuando una regla lleva una ventana a otro escritorio hay que avisar, para poder ofrecer ir.
/// </summary>
public class RuleMoveAnnouncementTests
{
    private const nint Window = 5;

    private static (RuleApplicationService Service, FakeVirtualDesktopService Desktops, FakeWindowEventService Events, List<WindowMovedEventArgs> Avisos)
        Build(string desktop, int windowOn = 0, int viewing = 0)
    {
        FakeWindowManager windows = new();
        windows.Add(TestWindows.Create(handle: Window));

        FakeVirtualDesktopService desktops = FakeVirtualDesktopService.WithDesktops(3);
        desktops.WindowDesktops[Window] = desktops.Desktops[windowOn].Id;
        desktops.CurrentDesktop = desktops.Desktops[viewing].Id;

        FakeWindowEventService events = new();
        RuleEngine engine = new();
        engine.SetRules(new[]
        {
            new WindowRule
            {
                Name = "test",
                Match = new WindowMatcher { ProcessName = "notepad.exe" },
                Actions = new WindowActions { Desktop = desktop }
            }
        });

        RuleApplicationService service = new(engine, windows, desktops, events, ownProcessId: 999);

        List<WindowMovedEventArgs> avisos = new();
        service.WindowMovedToAnotherDesktop += (_, e) => avisos.Add(e);
        service.Start();

        return (service, desktops, events, avisos);
    }

    [Fact]
    public void Se_avisa_cuando_la_ventana_acaba_en_un_escritorio_que_no_se_esta_viendo()
    {
        // Se mira el 1 y la regla la manda al 3.
        (RuleApplicationService service, FakeVirtualDesktopService desktops, FakeWindowEventService events, List<WindowMovedEventArgs> avisos) =
            Build(desktop: "3", windowOn: 0, viewing: 0);

        events.RaiseShown(TestWindows.Create(handle: Window));

        WindowMovedEventArgs aviso = Assert.Single(avisos);
        Assert.Equal(desktops.Desktops[2].Id, aviso.Desktop.Id);
        Assert.Equal(Window, aviso.Window.Handle);

        service.Dispose();
    }

    [Fact]
    public void No_se_avisa_si_aterriza_donde_ya_se_esta_mirando()
    {
        // Se esta mirando el 2 y la regla la manda justo ahi.
        (RuleApplicationService service, _, FakeWindowEventService events, List<WindowMovedEventArgs> avisos) =
            Build(desktop: "2", windowOn: 0, viewing: 1);

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Empty(avisos);
        service.Dispose();
    }

    [Fact]
    public void No_se_avisa_si_no_hizo_falta_moverla()
    {
        (RuleApplicationService service, _, FakeWindowEventService events, List<WindowMovedEventArgs> avisos) =
            Build(desktop: "1", windowOn: 0, viewing: 0);

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Empty(avisos);
        service.Dispose();
    }

    [Fact]
    public void No_se_avisa_al_aplicar_las_reglas_a_las_ventanas_ya_abiertas()
    {
        // Al arrancar se recorren todas las ventanas: preguntar por cada una seria una tortura.
        (RuleApplicationService service, FakeVirtualDesktopService desktops, _, List<WindowMovedEventArgs> avisos) =
            Build(desktop: "3", windowOn: 0, viewing: 0);

        service.ApplyToExistingWindows();

        Assert.Single(desktops.Moves);
        Assert.Empty(avisos);

        service.Dispose();
    }

    [Fact]
    public void La_regla_mueve_pero_nunca_cambia_de_escritorio_por_su_cuenta()
    {
        (RuleApplicationService service, FakeVirtualDesktopService desktops, FakeWindowEventService events, _) =
            Build(desktop: "3", windowOn: 0, viewing: 0);

        events.RaiseShown(TestWindows.Create(handle: Window));

        Assert.Single(desktops.Moves);
        Assert.Empty(desktops.Switches);

        service.Dispose();
    }
}
