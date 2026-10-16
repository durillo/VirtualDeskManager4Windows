using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Commands.Handlers;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class CommandDispatcherTests
{
    [Fact]
    public async Task An_unknown_command_type_is_rejected()
    {
        CommandDispatcher dispatcher = new();

        OperationResult result = await dispatcher.DispatchAsync(
            new CommandDefinition(CommandType.None),
            CommandContext.FromHotkey());

        Assert.Equal(OperationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task A_command_without_a_handler_is_unsupported()
    {
        CommandDispatcher dispatcher = new();

        OperationResult result = await dispatcher.DispatchAsync(
            new CommandDefinition(CommandType.TogglePinWindow),
            CommandContext.FromTray());

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.False(dispatcher.IsSupported(CommandType.TogglePinWindow));
    }

    [Fact]
    public async Task Toggle_always_on_top_uses_the_foreground_window()
    {
        FakeWindowManager windows = new();
        windows.Add(TestWindows.Create(handle: 7, topMost: false));
        windows.Foreground = 7;

        CommandDispatcher dispatcher = new();
        dispatcher.Register(new ToggleAlwaysOnTopCommandHandler(windows));

        OperationResult result = await dispatcher.DispatchAsync(
            new CommandDefinition(CommandType.ToggleAlwaysOnTop),
            CommandContext.FromHotkey());

        Assert.True(result.IsSuccess);
        (nint Handle, bool Enabled) call = windows.AlwaysOnTopCalls.Single();
        Assert.Equal((nint)7, call.Handle);
        Assert.True(call.Enabled);
    }

    [Fact]
    public async Task Toggle_always_on_top_turns_the_flag_off_again()
    {
        FakeWindowManager windows = new();
        windows.Add(TestWindows.Create(handle: 7, topMost: true));
        windows.Foreground = 7;

        CommandDispatcher dispatcher = new();
        dispatcher.Register(new ToggleAlwaysOnTopCommandHandler(windows));

        await dispatcher.DispatchAsync(
            new CommandDefinition(CommandType.ToggleAlwaysOnTop),
            CommandContext.FromHotkey());

        Assert.False(windows.AlwaysOnTopCalls.Single().Enabled);
    }

    [Fact]
    public async Task The_enabled_parameter_forces_the_state()
    {
        FakeWindowManager windows = new();
        windows.Add(TestWindows.Create(handle: 7, topMost: true));
        windows.Foreground = 7;

        CommandDispatcher dispatcher = new();
        dispatcher.Register(new ToggleAlwaysOnTopCommandHandler(windows));

        CommandDefinition command = new(CommandType.ToggleAlwaysOnTop);
        command.Parameters["enabled"] = "true";

        await dispatcher.DispatchAsync(command, CommandContext.FromUi());

        Assert.True(windows.AlwaysOnTopCalls.Single().Enabled);
    }

    [Fact]
    public async Task Without_a_foreground_window_the_result_is_invalid_window()
    {
        FakeWindowManager windows = new() { Foreground = 0 };

        CommandDispatcher dispatcher = new();
        dispatcher.Register(new ToggleAlwaysOnTopCommandHandler(windows));

        OperationResult result = await dispatcher.DispatchAsync(
            new CommandDefinition(CommandType.ToggleAlwaysOnTop),
            CommandContext.FromHotkey());

        Assert.Equal(OperationStatus.InvalidWindow, result.Status);
    }

    [Fact]
    public async Task An_exception_in_a_handler_becomes_a_failed_result()
    {
        CommandDispatcher dispatcher = new();
        dispatcher.Register(new ThrowingHandler());

        OperationResult result = await dispatcher.DispatchAsync(
            new CommandDefinition(CommandType.RestoreWindowLayout),
            CommandContext.FromUi());

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal("boom", result.Error);
    }

    private sealed class ThrowingHandler : ICommandHandler
    {
        public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[] { CommandType.RestoreWindowLayout };

        public Task<OperationResult> ExecuteAsync(
            CommandDefinition command,
            CommandContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
