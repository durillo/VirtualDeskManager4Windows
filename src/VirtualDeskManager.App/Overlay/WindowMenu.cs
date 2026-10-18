using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.Overlay;

/// <summary>
/// Las entradas del menú de una ventana, las mismas desde el botón de la barra de título y desde
/// la rejilla de escritorios.
/// </summary>
/// <remarks>
/// Cada entrada lanza un comando o un servicio; aquí no se toca ninguna ventana (regla de
/// arquitectura 1). Una capacidad que no está se enseña desactivada y con el motivo (§14).
/// </remarks>
public sealed class WindowMenu
{
    private readonly IVirtualDesktopService _desktops;
    private readonly IWindowManager _windows;
    private readonly Func<CommandDefinition, nint, Task<OperationResult>> _execute;

    /// <summary>Junta lo que necesitan las entradas; los menús se construyen después, uno a uno.</summary>
    /// <param name="desktops">Los escritorios y lo que el backend sabe hacer con ellos.</param>
    /// <param name="windows">Para consultar la ventana sobre la que se abre el menú.</param>
    /// <param name="execute">Ejecuta una orden sobre la ventana que se le pasa.</param>
    public WindowMenu(
        IVirtualDesktopService desktops,
        IWindowManager windows,
        Func<CommandDefinition, nint, Task<OperationResult>> execute)
    {
        _desktops = desktops;
        _windows = windows;
        _execute = execute;
    }

    /// <summary>Lo que se hace después de una acción, por ejemplo volver a pintar la rejilla.</summary>
    public Action? AfterAction { get; set; }

    /// <summary>«Siempre visible», con la marca puesta si la ventana ya lo está.</summary>
    /// <param name="target">La ventana sobre la que actúa.</param>
    /// <param name="isTopMost">Si ya está siempre visible; lo sabe quien abre el menú.</param>
    public MenuItem AlwaysOnTop(nint target, bool isTopMost)
    {
        MenuItem item = new()
        {
            Header = Loc.Get("Overlay.AlwaysOnTop"),
            IsCheckable = true,
            IsChecked = isTopMost
        };

        item.Click += (_, _) => Run(new CommandDefinition(CommandType.ToggleAlwaysOnTop), target);
        return item;
    }

    /// <summary>Show this window on every desktop, with a tick when it already is.</summary>
    public MenuItem ShowOnAllDesktops(nint target)
    {
        bool available = _desktops.Capabilities.CanPinWindow;
        bool? pinned = available ? _desktops.IsWindowPinned(target) : null;

        MenuItem item = new()
        {
            Header = Loc.Get("Overlay.ShowOnAllDesktops"),
            IsCheckable = available,
            IsChecked = pinned ?? false
        };

        if (available)
        {
            item.Click += (_, _) => Run(new CommandDefinition(CommandType.TogglePinWindow), target);
        }
        else
        {
            Disable(item, Loc.Get("Overlay.RequiresAdvancedBackend"));
        }

        return item;
    }

    /// <summary>
    /// Submenu with the real desktops, in the order Task View shows them. The one the window is
    /// already on is ticked and does nothing.
    /// </summary>
    public MenuItem MoveToDesktop(nint target)
    {
        MenuItem parent = new() { Header = Loc.Get("Overlay.MoveToDesktop") };

        if (!_desktops.Capabilities.CanMoveWindow || !_desktops.Capabilities.CanEnumerateDesktops)
        {
            Disable(parent, Loc.Get("Overlay.RequiresAdvancedBackend"));
            return parent;
        }

        IReadOnlyList<VirtualDesktopInfo> desktops = _desktops.GetDesktops();
        Guid? currentDesktop = _desktops.GetWindowDesktopId(target);

        if (desktops.Count == 0)
        {
            Disable(parent, Loc.Get("Overlay.RequiresAdvancedBackend"));
            return parent;
        }

        foreach (VirtualDesktopInfo desktop in desktops)
        {
            bool here = desktop.Id == currentDesktop;

            MenuItem item = new()
            {
                Header = desktop.HasName
                    ? desktop.Name
                    : Loc.Format("Overlay.DesktopNumber", desktop.Number),
                IsCheckable = true,
                IsChecked = here,
                IsEnabled = !here
            };

            Guid id = desktop.Id;

            item.Click += (_, _) =>
            {
                CommandDefinition command = new(CommandType.MoveWindowToDesktop);
                command.Parameters["desktop"] = id.ToString();
                Run(command, target);
            };

            parent.Items.Add(item);
        }

        return parent;
    }

    /// <summary>
    /// Disables an entry and explains why (spec section 14: a missing capability is never an
    /// exception). Without ShowOnDisabled WPF swallows the tooltip, which is exactly the text
    /// the user needs there.
    /// </summary>
    public static void Disable(MenuItem item, string why)
    {
        item.IsEnabled = false;
        item.ToolTip = why;
        ToolTipService.SetShowOnDisabled(item, true);
    }

    private async void Run(CommandDefinition command, nint target)
    {
        await _execute(command, target);
        AfterAction?.Invoke();
    }
}
