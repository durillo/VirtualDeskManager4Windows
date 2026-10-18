using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>El grupo «Actualizaciones» de General.</summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private readonly UpdateService _updates;
    private readonly AppHost _host;

    /// <summary>Se queda escuchando al servicio para repintar el estado.</summary>
    public UpdatesViewModel(UpdateService updates, AppHost host)
    {
        _updates = updates;
        _host = host;

        CheckCommand = new RelayCommand(() => _ = _updates.CheckAsync(manual: true));
        InstallCommand = new RelayCommand(() => _ = _updates.InstallAsync(), () => _updates.Available is not null);

        _updates.Changed += (_, _) =>
        {
            Raise(nameof(Status));
            Raise(nameof(IsBusy));
            Raise(nameof(HasUpdate));
            Raise(nameof(Notes));
            Raise(nameof(NotesTitle));
            InstallCommand.RaiseCanExecuteChanged();
        };
    }

    private UpdateSettings Settings => _host.Settings.Updates;

    /// <summary>La versión de esta Vdesk.</summary>
    public string Current => _updates.CurrentVersion;

    /// <summary>Cómo está: buscando, al día, hay una nueva, bajando…</summary>
    public string Status => _updates.Status.Length > 0 ? _updates.Status : Loc.Format("Update.Current", Current);

    /// <summary>Que está buscando o bajando: sale la barra de «Cargando…».</summary>
    public bool IsBusy => _updates.IsBusy;

    /// <summary>Que hay una versión nueva comprobada.</summary>
    public bool HasUpdate => _updates.Available is not null;

    /// <summary>El título de las novedades de la versión nueva.</summary>
    public string NotesTitle => _updates.Available is { } a ? Loc.Format("Update.WhatsNew", a.Version) : "";

    /// <summary>Las novedades, tal como vienen (Markdown sencillo).</summary>
    public string Notes => _updates.Available?.Notes ?? "";

    /// <summary>«Buscar actualizaciones».</summary>
    public RelayCommand CheckCommand { get; }

    /// <summary>«Actualizar ahora».</summary>
    public RelayCommand InstallCommand { get; }

    /// <summary>Buscar solas al arrancar y una vez al día.</summary>
    public bool CheckAutomatically
    {
        get => Settings.CheckAutomatically;
        set
        {
            if (Settings.CheckAutomatically == value)
            {
                return;
            }

            Settings.CheckAutomatically = value;
            _host.Configuration.SaveSettings();
            Raise();
        }
    }

    /// <summary>
    /// Instalar sola la versión nueva cuando no se esté usando el equipo. Al encenderlo se busca
    /// enseguida.
    /// </summary>
    public bool InstallAutomatically
    {
        get => Settings.InstallAutomatically;
        set
        {
            if (Settings.InstallAutomatically == value)
            {
                return;
            }

            Settings.InstallAutomatically = value;
            _host.Configuration.SaveSettings();
            Raise();

            if (value)
            {
                _ = _updates.CheckAsync(manual: false);
            }
        }
    }

    /// <summary>Que esta Vdesk se puede actualizar sola: la del instalador, no el .exe suelto.</summary>
    public bool CanInstallAutomatically => UpdateService.CanPatchInPlace;

    /// <summary>Recibir versiones de prueba. Al encenderlo se busca enseguida.</summary>
    public bool Beta
    {
        get => Settings.Beta;
        set
        {
            if (Settings.Beta == value)
            {
                return;
            }

            Settings.Beta = value;
            _host.Configuration.SaveSettings();
            Raise();
            _ = _updates.CheckAsync(manual: false);
        }
    }
}
