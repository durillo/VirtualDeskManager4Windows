using System;
using System.Collections.Generic;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.Tray;

/// <summary>
/// System tray icon and menu (spec section 32). The menu only raises events and command
/// requests: it never touches Win32 or the services directly.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;
    private readonly System.Windows.Forms.ToolStripMenuItem _pauseItem;

    /// <summary>Menu entries with the translation key they show, so the language can change live.</summary>
    private readonly List<(System.Windows.Forms.ToolStripItem Item, string Key)> _localized = new();

    private System.Drawing.Icon _icon;
    private bool _disposed;
    private bool _paused;

    /// <summary>Escritorio que muestra el icono ahora mismo. Cero es "ninguno, icono normal".</summary>
    private int _desktopNumber;
    private string _desktopName = "";

    /// <summary>Monta el menú y pone el icono en la bandeja, ya visible y en el idioma actual.</summary>
    public TrayService()
    {
        _icon = TrayIconFactory.Create();
        _menu = new System.Windows.Forms.ContextMenuStrip();

        System.Windows.Forms.ToolStripMenuItem header = new(AppVersion.ProductName) { Enabled = false };
        _menu.Items.Add(header);
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        Add("Tray.PreviousDesktop", () => Request(CommandType.SwitchDesktopLeft));
        Add("Tray.NextDesktop", () => Request(CommandType.SwitchDesktopRight));
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        Add("Tray.ToggleAlwaysOnTop", () => Request(CommandType.ToggleAlwaysOnTop));
        Add("Tray.TogglePinWindow", () => Request(CommandType.TogglePinWindow));
        Add("Tray.AlwaysOpenHere", () => AlwaysOpenHereRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        Add("Tray.Vdesk", () => VdeskRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        // Sólo cuando hay una versión nueva (ShowUpdate).
        _updateItem = new System.Windows.Forms.ToolStripMenuItem { Visible = false, Font = new System.Drawing.Font(_menu.Font, System.Drawing.FontStyle.Bold) };
        _updateItem.Click += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_updateItem);

        Add("Tray.OpenSettings", () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));

        _pauseItem = new System.Windows.Forms.ToolStripMenuItem(Loc.Get("Tray.PauseRules")) { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => PauseRulesChanged?.Invoke(this, _pauseItem.Checked);
        _menu.Items.Add(_pauseItem);
        _localized.Add((_pauseItem, "Tray.PauseRules"));

        Add("Tray.ReloadConfiguration", () => ReloadRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        Add("Tray.Exit", () => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _icon,
            Text = AppVersion.ProductName,
            Visible = true,
            ContextMenuStrip = _menu
        };

        // Un clic (izquierdo) abre la ventana principal; el derecho, el menú, como siempre.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                VdeskRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        // Un aviso con algo detrás: pincharlo lo abre. Sólo el último, y
        // sólo mientras se ve; al cerrarse, pinchar el icono ya no hace eso.
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            Action? accion = _balloonClick;
            _balloonClick = null;
            accion?.Invoke();
        };
        _notifyIcon.BalloonTipClosed += (_, _) => _balloonClick = null;

        ApplyLanguage();
        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>«Abrir ajustes» en el menú.</summary>
    public event EventHandler? OpenSettingsRequested;
    /// <summary>«Salir» en el menú.</summary>
    public event EventHandler? ExitRequested;
    /// <summary>«Recargar configuración» en el menú.</summary>
    public event EventHandler? ReloadRequested;
    /// <summary>Se marcó o desmarcó «Pausar reglas». Lleva el estado nuevo: <c>true</c> es en pausa.</summary>
    public event EventHandler<bool>? PauseRulesChanged;
    /// <summary>Una entrada del menú que es una orden sin más (cambiar de escritorio, siempre visible…).</summary>
    public event EventHandler<CommandType>? CommandRequested;

    /// <summary>"Always open the active window on this desktop".</summary>
    public event EventHandler? AlwaysOpenHereRequested;

    /// <summary>Se pidió la ventana principal: en el menú o con un clic en el icono.</summary>
    public event EventHandler? VdeskRequested;

    /// <summary>Se pidió instalar la versión nueva desde la bandeja.</summary>
    public event EventHandler? UpdateRequested;

    private readonly System.Windows.Forms.ToolStripMenuItem _updateItem;

    /// <summary>Enseña «Actualizar a la X» en el menú, o lo quita con nulo.</summary>
    public void ShowUpdate(string? version)
    {
        _updateItem.Visible = version is not null;
        _updateItem.Text = version is null ? "" : Loc.Format("Tray.Update", version);
    }

    /// <summary>La marca de «Pausar reglas». Cambiarla desde fuera también lanza <see cref="PauseRulesChanged"/>.</summary>
    public bool RulesPaused
    {
        get => _pauseItem.Checked;
        set
        {
            if (_pauseItem.Checked != value)
            {
                _pauseItem.Checked = value;
            }
        }
    }

    private Action? _balloonClick;

    /// <summary>Short feedback for actions that have no visible effect, e.g. an unsupported command.</summary>
    /// <param name="title">El título del aviso.</param>
    /// <param name="message">El texto.</param>
    /// <param name="onClick">Lo que pasa si se pincha el aviso ; nada si es nulo.</param>
    public void Notify(string title, string message, Action? onClick = null)
    {
        if (_disposed)
        {
            return;
        }

        _balloonClick = onClick;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    /// <summary>Pone el icono apagado mientras las reglas estén en pausa, conservando el escritorio si lo había.</summary>
    public void UpdateIcon(bool paused)
    {
        if (_disposed)
        {
            return;
        }

        _paused = paused;
        Redraw();
    }

    /// <summary>
    /// Pone el escritorio actual en el icono y en el texto que sale al pasar el ratón.
    /// </summary>
    /// <remarks>
    /// El número va en el icono y el nombre en el tooltip, y no es un capricho: Windows acaba
    /// enseñando el icono a 16 píxeles, donde un nombre no cabe de ninguna manera, mientras que
    /// el tooltip admite texto de sobra.
    ///
    /// El tooltip tiene un tope de 63 caracteres. Pasarse de ahí no da error: el icono
    /// desaparece de la bandeja, que es una forma pésima de enterarse.
    /// </remarks>
    /// <param name="number">Número del escritorio, empezando en 1. Cero lo desconoce.</param>
    /// <param name="name">Nombre del escritorio, o vacío si no tiene.</param>
    public void UpdateDesktop(int number, string name)
    {
        if (_disposed)
        {
            return;
        }

        if (_desktopNumber == number && string.Equals(_desktopName, name, StringComparison.Ordinal))
        {
            return;
        }

        _desktopNumber = number;
        _desktopName = name ?? "";

        Redraw();
    }

    /// <summary>Deja de mostrar el escritorio y vuelve al icono de siempre.</summary>
    public void ClearDesktop()
    {
        if (_disposed || _desktopNumber == 0)
        {
            return;
        }

        _desktopNumber = 0;
        _desktopName = "";
        Redraw();
    }

    private void Redraw()
    {
        System.Drawing.Icon previous = _icon;

        _icon = _desktopNumber > 0
            ? TrayIconFactory.CreateWithNumber(_desktopNumber, _paused)
            : TrayIconFactory.Create(_paused);

        _notifyIcon.Icon = _icon;
        _notifyIcon.Text = Tooltip();

        previous.Dispose();
    }

    private string Tooltip()
    {
        const int Limit = 63;

        if (_desktopNumber <= 0)
        {
            return AppVersion.ProductName;
        }

        string desktop = _desktopName.Length > 0
            ? Loc.Format("Tray.DesktopNamed", _desktopNumber, _desktopName)
            : Loc.Format("Overlay.DesktopNumber", _desktopNumber);

        string text = AppVersion.ProductName + " — " + desktop;

        return text.Length <= Limit ? text : text[..Limit];
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        foreach ((System.Windows.Forms.ToolStripItem item, string key) in _localized)
        {
            item.Text = Loc.Get(key);
        }

        // Arabic and Urdu also mirror the menu.
        _menu.RightToLeft = Loc.Instance.IsRightToLeft
            ? System.Windows.Forms.RightToLeft.Yes
            : System.Windows.Forms.RightToLeft.No;
    }

    private void Add(string key, Action action)
    {
        System.Windows.Forms.ToolStripMenuItem item = new(Loc.Get(key));
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
        _localized.Add((item, key));
    }

    private void Request(CommandType type) => CommandRequested?.Invoke(this, type);

    /// <summary>Quita el icono de la bandeja y libera el menú y el icono.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
