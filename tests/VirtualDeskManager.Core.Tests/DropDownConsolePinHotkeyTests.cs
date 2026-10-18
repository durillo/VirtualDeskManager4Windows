using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Commands.Handlers;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>El atajo que fija la consola desplegable para que no se recoja sola.</summary>
public class DropDownConsolePinHotkeyTests
{
    [Fact]
    public void Es_Ctrl_Alt_Mayus_y_la_misma_tecla_que_la_despliega()
    {
        HotkeyDefinition pin = DefaultConfiguration.CreateDropDownConsolePinHotkeys().Single();
        HotkeyDefinition consola = DefaultConfiguration.CreateDropDownConsoleHotkeys().Single();

        Assert.Equal(CommandType.ToggleDropDownConsolePin, pin.Command.Type);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, pin.Modifiers);
        Assert.Equal(consola.Key, pin.Key);
        Assert.True(pin.IsValid);
    }

    [Fact]
    public void Esta_en_los_de_serie_sin_chocar_con_ninguno()
    {
        List<HotkeyDefinition> defaults = DefaultConfiguration.CreateHotkeys();

        Assert.Contains(defaults, h => h.Command.Type == CommandType.ToggleDropDownConsolePin);
        Assert.Equal(defaults.Count, defaults.Select(h => (h.Modifiers, h.Key)).Distinct().Count());
    }

    [Fact]
    public void Se_anade_a_una_configuracion_anterior_y_solo_una_vez()
    {
        List<HotkeyDefinition> existing = DefaultConfiguration.CreateHotkeys()
            .Where(h => h.Command.Type != CommandType.ToggleDropDownConsolePin)
            .ToList();

        Assert.Single(HotkeyMigrations.AddDropDownConsolePinHotkeys(existing));
        Assert.Empty(HotkeyMigrations.AddDropDownConsolePinHotkeys(existing));
    }

    [Fact]
    public void No_le_quita_la_combinacion_a_un_atajo_del_usuario()
    {
        List<HotkeyDefinition> existing = DefaultConfiguration.CreateHotkeys()
            .Where(h => h.Command.Type != CommandType.ToggleDropDownConsolePin)
            .ToList();

        existing.Add(new HotkeyDefinition
        {
            Id = "mio",
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            Key = HotkeyKey.Oem5,
            Command = new CommandDefinition(CommandType.ToggleDesktopGrid)
        });

        Assert.Empty(HotkeyMigrations.AddDropDownConsolePinHotkeys(existing));
    }

    [Fact]
    public async Task El_comando_fija_y_el_otro_despliega()
    {
        FakeConsole console = new();
        DropDownConsoleCommandHandler handler = new(console);

        await handler.ExecuteAsync(new CommandDefinition(CommandType.ToggleDropDownConsolePin), CommandContext.FromHotkey());
        Assert.True(console.IsPinned);
        Assert.False(console.IsOpen);

        await handler.ExecuteAsync(new CommandDefinition(CommandType.ToggleDropDownConsole), CommandContext.FromHotkey());
        Assert.True(console.IsOpen);
        Assert.True(console.IsPinned);
    }

    private sealed class FakeConsole : IDropDownConsole
    {
        public bool IsOpen { get; private set; }

        public bool IsPinned { get; private set; }

        public OperationResult Show()
        {
            IsOpen = true;
            return OperationResult.Ok();
        }

        public OperationResult Hide()
        {
            IsOpen = false;
            return OperationResult.Ok();
        }

        public OperationResult Toggle() => IsOpen ? Hide() : Show();

        public OperationResult TogglePin()
        {
            IsPinned = !IsPinned;
            return OperationResult.Ok();
        }
    }
}
