using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// Remembering how many desktops the user had and putting them back when Windows loses them.
/// </summary>
public class DesktopLayoutTests
{
    private static (DesktopLayoutService Service, FakeVirtualDesktopService Desktops, FakeConfigurationService Configuration)
        Build(int existing, params string[] saved)
    {
        FakeVirtualDesktopService desktops = FakeVirtualDesktopService.WithDesktops(existing);
        FakeConfigurationService configuration = new();

        if (saved.Length > 0)
        {
            configuration.Settings.Desktops = new DesktopLayout { Names = saved.ToList() };
        }

        return (new DesktopLayoutService(desktops, configuration), desktops, configuration);
    }

    [Fact]
    public void The_desktops_that_are_open_get_saved()
    {
        (DesktopLayoutService service, _, FakeConfigurationService configuration) = Build(3);

        Assert.True(service.Remember());
        Assert.Equal(3, configuration.Settings.Desktops.Count);
        Assert.Equal("Main", configuration.Settings.Desktops.Names[0]);
    }

    [Fact]
    public void Saving_the_same_thing_twice_does_not_write_the_file_again()
    {
        (DesktopLayoutService service, _, FakeConfigurationService configuration) = Build(3);

        service.Remember();
        int writes = configuration.Saves;

        Assert.False(service.Remember());
        Assert.Equal(writes, configuration.Saves);
    }

    // The case that started all this: Windows drops the desktops and every rule naming one by
    // number starts failing.
    [Fact]
    public void The_missing_desktops_are_created_again()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, _) =
            Build(1, "Main", "Correo", "Desarrollo", "Pruebas");

        DesktopLayoutService.RestoreOutcome outcome = service.Restore();

        Assert.False(outcome.Skipped);
        Assert.Equal(3, outcome.Created);
        Assert.Equal(4, desktops.GetDesktops().Count);
    }

    [Fact]
    public void The_names_come_back_too()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, _) =
            Build(1, "Main", "Correo", "Desarrollo");

        service.Restore();

        Assert.Equal(new[] { "Main", "Correo", "Desarrollo" }, desktops.GetDesktops().Select(d => d.Name));
    }

    // Creating a desktop drags the view onto it. Starting the application must not leave the user
    // somewhere they did not ask to be.
    [Fact]
    public void The_user_is_left_on_the_desktop_they_were_looking_at()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, _) =
            Build(1, "Main", "Correo", "Desarrollo");

        Guid origin = desktops.GetDesktops()[0].Id;

        service.Restore();

        Assert.Equal(origin, desktops.CurrentDesktop);
    }

    [Fact]
    public void Having_more_desktops_than_were_saved_removes_none()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, _) = Build(5, "Main", "Correo");

        DesktopLayoutService.RestoreOutcome outcome = service.Restore();

        Assert.Equal(0, outcome.Created);
        Assert.Equal(5, desktops.GetDesktops().Count);
    }

    [Fact]
    public void Nothing_happens_when_the_user_turned_it_off()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, FakeConfigurationService configuration) =
            Build(1, "Main", "Correo");

        configuration.Settings.RestoreDesktopLayout = false;

        Assert.True(service.Restore().Skipped);
        Assert.Single(desktops.GetDesktops());
    }

    [Fact]
    public void Nothing_happens_on_the_very_first_run()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, _) = Build(2);

        Assert.True(service.Restore().Skipped);
        Assert.Equal(2, desktops.GetDesktops().Count);
    }

    // Synthetic input can simply not arrive. Half a layout is better than an exception, and what
    // ends up saved has to be what exists, not what was wanted.
    [Fact]
    public void A_shell_that_stops_creating_desktops_leaves_the_rest_alone()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, FakeConfigurationService configuration) =
            Build(1, "Main", "Correo", "Desarrollo", "Pruebas");

        desktops.FailCreateAfter = 3;

        DesktopLayoutService.RestoreOutcome outcome = service.Restore();

        Assert.Equal(2, outcome.Created);
        Assert.Equal(3, desktops.GetDesktops().Count);
        Assert.Equal(3, configuration.Settings.Desktops.Count);
    }

    // A name saved as empty means "it never had one", which is no reason to wipe one the user has
    // given the desktop since.
    [Fact]
    public void An_empty_saved_name_does_not_erase_the_current_one()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, _) = Build(2, "Main", "");

        desktops.Desktops[1] = new VirtualDesktopInfo(desktops.Desktops[1].Id, 1, "Correo");

        service.Restore();

        Assert.Equal("Correo", desktops.GetDesktops()[1].Name);
    }

    [Fact]
    public void A_backend_that_cannot_create_desktops_says_so_instead_of_trying()
    {
        (DesktopLayoutService service, FakeVirtualDesktopService desktops, _) = Build(1, "Main", "Correo");

        desktops.Capabilities = new VirtualDesktopCapabilities { CanEnumerateDesktops = true };

        Assert.True(service.Restore().Skipped);
        Assert.Single(desktops.GetDesktops());
    }
}
