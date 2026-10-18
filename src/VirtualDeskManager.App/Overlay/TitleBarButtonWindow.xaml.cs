using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Commands;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App.Overlay;

/// <summary>
/// The floating button that sits on the title bar of the active window (a caption button of our
/// own) and opens the per window menu.
/// </summary>
/// <remarks>
/// It is a window of this application, not a real caption button: adding a button to the caption
/// of another process would require injecting a DLL into it, which section 36 forbids. It is a
/// tool window that never takes the focus, so the window underneath stays active, and every
/// command carries the tracked handle so the action always lands on the right window even if the
/// focus moved in the meantime.
/// </remarks>
public partial class TitleBarButtonWindow : Window
{
    private static readonly Brush DarkGlyph = Frozen(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly Brush LightGlyph = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>Lo que el usuario haya elegido en los ajustes.</summary>
    private readonly Func<TitleBarButtonSettings> _style;

    private readonly IWindowManager _windows;
    private readonly IVirtualDesktopService _desktops;
    private readonly Action _openSettings;
    private readonly RememberWindowService? _remember;
    private readonly Action<string>? _notify;
    private readonly WindowMenu _menuItems;

    private nint _handle;
    private TitleBarAnchor? _anchor;
    private ContextMenu? _menu;

    /// <summary>
    /// Crea el botón con su ventana nativa ya hecha y sin activarse, para poder colocarlo antes de
    /// enseñarlo.
    /// </summary>
    /// <param name="windows">Para colocar el botón y actuar sobre la ventana.</param>
    /// <param name="desktops">Los escritorios que salen en el menú.</param>
    /// <param name="execute">Ejecuta una orden sobre la ventana que se le pasa.</param>
    /// <param name="openSettings">Abre la ventana de ajustes.</param>
    /// <param name="style">El aspecto elegido por el usuario; sin él, el de serie.</param>
    /// <param name="remember">Para guardar como regla lo que se haga con la ventana; sin él, la entrada sale desactivada.</param>
    /// <param name="notify">Enseña un aviso al usuario.</param>
    public TitleBarButtonWindow(
        IWindowManager windows,
        IVirtualDesktopService desktops,
        Func<CommandDefinition, nint, Task<OperationResult>> execute,
        Action openSettings,
        Func<TitleBarButtonSettings>? style = null,
        RememberWindowService? remember = null,
        Action<string>? notify = null)
    {
        InitializeComponent();

        _windows = windows;
        _desktops = desktops;
        _openSettings = openSettings;
        _style = style ?? (() => new TitleBarButtonSettings());
        _remember = remember;
        _notify = notify;
        _menuItems = new WindowMenu(desktops, windows, execute);

        // The handle is created up front so the button can be positioned before it is ever shown
        // and never flashes at the wrong place.
        _handle = new WindowInteropHelper(this).EnsureHandle();
        _windows.SetNonActivating(_handle, true);

        // There is deliberately no handler on Deactivated. The menu's own popup takes the
        // activation away from this window the moment it opens, so closing the menu on
        // deactivation made it vanish as soon as the pointer moved. Letting the window be
        // activated while the menu is open is enough: from there WPF dismisses it on its own
        // when the user clicks anywhere outside it, or presses Escape.

        Root.MouseLeftButtonUp += (_, _) => OpenMenu();
        Root.MouseEnter += (_, _) => UpdateBrushes(hovering: true);
        Root.MouseLeave += (_, _) => UpdateBrushes(hovering: false);
    }

    /// <summary>Moves the button, or hides it when there is no window to attach it to.</summary>
    public void Update(TitleBarAnchor? anchor)
    {
        // While the menu is open the button stays exactly where it is. Opening the menu activates
        // this window, so our own process becomes the foreground one and the tracker rightly
        // answers "there is no window to attach to"; obeying that would hide the owner of the
        // menu and take the menu down with it. This is what made the menu vanish at the first
        // twitch of the mouse.
        if (_menu is { IsOpen: true })
        {
            return;
        }

        if (anchor is not TitleBarAnchor target)
        {
            _anchor = null;

            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        _anchor = target;
        UpdateBrushes(hovering: false);

        // Show first, position afterwards. WPF re-applies the Left and Top of the XAML when a
        // window is shown, so positioning before showing would send the button back to its
        // parking place off screen and it would only appear once a second event arrived.
        if (!IsVisible)
        {
            Show();
        }

        // Positioning goes through the window service: the interface never calls Win32 itself
        // (architecture rule 1). Physical pixels, so no DPI conversion is needed.
        // It climbs back to the top every time: a window that has just been set to always on top
        // joins the same band and would otherwise cover the button.
        _windows.PlaceOnTop(_handle, target.X, target.Y, target.Size, target.Size);
    }

    /// <summary>Esconde el botón y olvida la ventana a la que estaba pegado. Con el menú abierto no hace nada.</summary>
    public void HideButton() => Update(null);

    /// <summary>
    /// Pinta el botón con el color del usuario y la opacidad que toque según el estado.
    /// </summary>
    /// <remarks>
    /// Los tres estados se distinguen sólo por opacidad y no por color, para que el color elegido
    /// siga siendo el que se ve. La versión anterior pintaba de azul la ventana fijada siempre
    /// visible, lo cual estaba bien mientras el color era fijo, pero con uno elegido a mano
    /// significaría ignorarlo justo en el estado que más se mira.
    /// </remarks>
    private void UpdateBrushes(bool hovering)
    {
        bool active = _anchor?.IsTopMost ?? false;
        TitleBarButtonSettings style = _style();
        RgbColour colour = style.ParsedColour;

        int opacity = active ? 100 : hovering ? style.HoverOpacityPercent : style.OpacityPercent;

        Root.Background = Fill(colour, opacity);
        Root.BorderBrush = Fill(colour.IsLight ? RgbColour.Grey : new RgbColour(0xFF, 0xFF, 0xFF), 38);

        // El dibujo de dentro sale del color de fondo y no de otro ajuste: sobre un fondo claro
        // tiene que ir oscuro y al revés, y eso no es una preferencia, es que si no, no se ve.
        Glyph.Stroke = colour.IsLight ? DarkGlyph : LightGlyph;

        Root.ToolTip = Loc.Get("Overlay.Tooltip");
    }

    private static Brush Frozen(Color colour)
    {
        SolidColorBrush brush = new(colour);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Fill(RgbColour colour, int opacityPercent)
    {
        byte alpha = (byte)Math.Clamp((opacityPercent * 255) / 100, 0, 255);

        SolidColorBrush brush = new(Color.FromArgb(alpha, colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }

    private void CloseMenu()
    {
        if (_menu is { IsOpen: true })
        {
            _menu.IsOpen = false;
        }
    }

    private void OpenMenu()
    {
        if (_anchor is not TitleBarAnchor target)
        {
            return;
        }

        CloseMenu();

        ContextMenu menu = new()
        {
            PlacementTarget = Root,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            FlowDirection = Loc.Instance.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };

        _menu = menu;

        // While the menu is open the button is allowed to take the focus, which is what lets the
        // menu close on its own when the user clicks anywhere else or presses Escape. The moment
        // it closes, the button goes back to never stealing the focus.
        _windows.SetNonActivating(_handle, false);
        Activate();

        menu.Closed += (_, _) =>
        {
            _windows.SetNonActivating(_handle, true);

            if (_anchor is TitleBarAnchor current)
            {
                _windows.PlaceOnTop(_handle, current.X, current.Y, current.Size, current.Size);

                // The window whose caption the menu came from goes back to being the active one.
                // Giving the foreground away is always allowed, because we hold it at this point.
                _windows.ActivateWindow(current.Handle);
            }
        };

        menu.Items.Add(_menuItems.AlwaysOnTop(target.Handle, target.IsTopMost));
        menu.Items.Add(_menuItems.ShowOnAllDesktops(target.Handle));
        menu.Items.Add(_menuItems.MoveToDesktop(target.Handle));
        menu.Items.Add(BuildRememberMenu(target.Handle));

        menu.Items.Add(new Separator());

        MenuItem settings = new() { Header = Loc.Get("Overlay.OpenSettings") };
        settings.Click += (_, _) => _openSettings();
        menu.Items.Add(settings);

        menu.IsOpen = true;
    }

    /// <summary>
    /// "Always open this application like this": writes a rule out of the window on screen
    /// (spec section 45).
    /// </summary>
    private MenuItem BuildRememberMenu(nint target)
    {
        MenuItem parent = new() { Header = Loc.Get("Overlay.AlwaysOpen") };

        if (_remember is null || !_desktops.Capabilities.CanEnumerateDesktops)
        {
            Disable(parent);
            return parent;
        }

        VirtualDesktopInfo? desktop = _remember.CurrentDesktopOf(target);

        if (desktop is null)
        {
            Disable(parent);
            return parent;
        }

        string desktopName = desktop.HasName ? desktop.Name : Loc.Format("Overlay.DesktopNumber", desktop.Number);

        MenuItem here = new() { Header = Loc.Format("Overlay.AlwaysOpenHere", desktopName) };
        here.Click += (_, _) => Remember(target, new WindowActions { Desktop = DesktopResolver.Describe(desktop) });
        parent.Items.Add(here);

        MenuItem asIs = new() { Header = Loc.Get("Overlay.AlwaysOpenLikeNow") };
        asIs.Click += (_, _) => Remember(target, _remember.CaptureState(target));
        parent.Items.Add(asIs);

        return parent;
    }

    private void Remember(nint target, WindowActions actions)
    {
        string? name = _remember?.Remember(target, actions);

        if (name is not null)
        {
            _notify?.Invoke(Loc.Format("Overlay.RuleSaved", name));
        }
    }

    /// <summary>
    /// Disables an entry and explains why (spec section 14: a missing capability is never an
    /// exception). Without ShowOnDisabled WPF swallows the tooltip, which is exactly the text
    /// the user needs there.
    /// </summary>
    private static void Disable(MenuItem item) => WindowMenu.Disable(item, Loc.Get("Overlay.RequiresAdvancedBackend"));


    /// <summary>Olvida el identificador de ventana, que ya no vale.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _handle = 0;
        base.OnClosed(e);
    }
}
