using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Commands.Handlers;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class DesktopCommandHandlerTests
{
    private const nint Window = 42;

    private static (DesktopCommandHandler Handler, FakeWindowManager Windows, FakeVirtualDesktopService Desktops)
        Build(int desktops = 3, bool follow = false)
    {
        FakeWindowManager windows = new();
        windows.Add(TestWindows.Create(handle: Window));
        windows.Foreground = Window;

        FakeVirtualDesktopService service = FakeVirtualDesktopService.WithDesktops(desktops);
        service.WindowDesktops[Window] = service.Desktops[1].Id;

        return (new DesktopCommandHandler(windows, service, () => follow), windows, service);
    }

    private static CommandDefinition Command(CommandType type, string? key = null, string? value = null)
    {
        CommandDefinition command = new(type);

        if (key is not null && value is not null)
        {
            command.Parameters[key] = value;
        }

        return command;
    }

    [Fact]
    public async Task Moves_the_window_to_the_desktop_given_by_identifier()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();
        Guid target = desktops.Desktops[2].Id;

        OperationResult result = await handler.ExecuteAsync(
            Command(CommandType.MoveWindowToDesktop, "desktop", target.ToString()),
            CommandContext.FromUi(Window));

        Assert.True(result.IsSuccess);
        Assert.Equal((Window, target), desktops.Moves.Single());
    }

    [Fact]
    public async Task Moves_the_window_to_the_desktop_given_by_number()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();

        await handler.ExecuteAsync(
            Command(CommandType.MoveWindowToDesktop, "number", "1"),
            CommandContext.FromUi(Window));

        Assert.Equal(desktops.Desktops[0].Id, desktops.Moves.Single().Desktop);
    }

    [Fact]
    public async Task Moves_the_window_to_the_desktop_on_the_left()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();

        await handler.ExecuteAsync(Command(CommandType.MoveWindowLeft), CommandContext.FromUi(Window));

        Assert.Equal(desktops.Desktops[0].Id, desktops.Moves.Single().Desktop);
    }

    [Fact]
    public async Task Moves_the_window_to_the_desktop_on_the_right()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();

        await handler.ExecuteAsync(Command(CommandType.MoveWindowRight), CommandContext.FromUi(Window));

        Assert.Equal(desktops.Desktops[2].Id, desktops.Moves.Single().Desktop);
    }

    [Fact]
    public async Task There_is_no_wrap_around_at_the_ends()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();
        desktops.WindowDesktops[Window] = desktops.Desktops[0].Id;

        OperationResult result = await handler.ExecuteAsync(
            Command(CommandType.MoveWindowLeft),
            CommandContext.FromUi(Window));

        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Empty(desktops.Moves);
    }

    [Fact]
    public async Task The_desktop_is_followed_only_when_the_setting_says_so()
    {
        (DesktopCommandHandler following, _, FakeVirtualDesktopService withFollow) = Build(follow: true);
        await following.ExecuteAsync(Command(CommandType.MoveWindowRight), CommandContext.FromUi(Window));
        Assert.Equal(withFollow.Desktops[2].Id, withFollow.Switches.Single());

        (DesktopCommandHandler staying, _, FakeVirtualDesktopService withoutFollow) = Build(follow: false);
        await staying.ExecuteAsync(Command(CommandType.MoveWindowRight), CommandContext.FromUi(Window));
        Assert.Empty(withoutFollow.Switches);
    }

    [Fact]
    public async Task Moving_to_the_desktop_it_is_already_on_does_nothing()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();
        Guid same = desktops.Desktops[1].Id;

        OperationResult result = await handler.ExecuteAsync(
            Command(CommandType.MoveWindowToDesktop, "desktop", same.ToString()),
            CommandContext.FromUi(Window));

        Assert.True(result.IsSuccess);
        Assert.Empty(desktops.Moves);
    }

    [Fact]
    public async Task A_command_without_a_target_desktop_fails_instead_of_moving_somewhere_random()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();

        OperationResult result = await handler.ExecuteAsync(
            Command(CommandType.MoveWindowToDesktop),
            CommandContext.FromUi(Window));

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Empty(desktops.Moves);
    }

    [Fact]
    public async Task Pinning_toggles_the_state()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();

        await handler.ExecuteAsync(Command(CommandType.TogglePinWindow), CommandContext.FromUi(Window));
        Assert.True(desktops.Pinned[Window]);

        await handler.ExecuteAsync(Command(CommandType.TogglePinWindow), CommandContext.FromUi(Window));
        Assert.False(desktops.Pinned[Window]);
    }

    [Fact]
    public async Task A_backend_without_capabilities_answers_unsupported()
    {
        FakeWindowManager windows = new();
        windows.Add(TestWindows.Create(handle: Window));

        FakeVirtualDesktopService desktops = new() { Capabilities = VirtualDesktopCapabilities.None };
        DesktopCommandHandler handler = new(windows, desktops);

        foreach (CommandType type in handler.SupportedTypes)
        {
            OperationResult result = await handler.ExecuteAsync(new CommandDefinition(type), CommandContext.FromUi(Window));
            Assert.Equal(OperationStatus.Unsupported, result.Status);
            Assert.False(handler.IsSupported(type));
        }
    }

    [Fact]
    public async Task A_window_that_is_gone_is_reported_as_invalid()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();

        OperationResult result = await handler.ExecuteAsync(
            Command(CommandType.MoveWindowRight),
            CommandContext.FromUi(9999));

        Assert.Equal(OperationStatus.InvalidWindow, result.Status);
        Assert.Empty(desktops.Moves);
    }

    [Fact]
    public async Task A_failure_from_the_backend_is_passed_through_and_nothing_is_followed()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build(follow: true);
        desktops.NextMoveResult = OperationResult.Denied();

        OperationResult result = await handler.ExecuteAsync(
            Command(CommandType.MoveWindowRight),
            CommandContext.FromUi(Window));

        Assert.Equal(OperationStatus.AccessDenied, result.Status);
        Assert.Empty(desktops.Switches);
    }

    [Fact]
    public async Task Switching_to_a_desktop_by_number_works()
    {
        (DesktopCommandHandler handler, _, FakeVirtualDesktopService desktops) = Build();

        OperationResult result = await handler.ExecuteAsync(
            Command(CommandType.SwitchToDesktop, "number", "3"),
            CommandContext.FromTray());

        Assert.True(result.IsSuccess);
        Assert.Equal(desktops.Desktops[2].Id, desktops.Switches.Single());
    }
}
