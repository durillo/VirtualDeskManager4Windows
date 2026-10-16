using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Updates;

namespace VirtualDeskManager.App;

/// <summary>
/// La actualización por internet: busca la versión nueva, avisa, y al pedirlo se baja sólo los
/// ficheros que cambian y los pone en su sitio.
/// </summary>
/// <remarks>
/// <para>
/// Pedido y decidido con el usuario el 2026-10-09: busca sola al arrancar y una vez al día, avisa en
/// la bandeja y pregunta antes de instalar; las versiones de prueba, sólo a quien las pide.
/// </para>
/// <para>
/// <b>Nada se instala sin comprobar la firma</b> del manifiesto (<see cref="UpdateSignature"/>) y
/// la huella de cada fichero bajado: quien entre en el servidor no puede colar nada. Vdesk no lleva
/// ninguna credencial del servidor; sólo lee por HTTPS.
/// </para>
/// <para>
/// Aplicar: los ficheros nuevos se bajan a una carpeta aparte, y un PowerShell espera a que Vdesk
/// se cierre, los copia encima con robocopy y lo vuelve a abrir. La aplicación está en la carpeta
/// del usuario (MSI por usuario), así que no hace falta administrador (§36). Una Vdesk que no es la
/// del instalador (el .exe suelto) no se toca: se le ofrece descargar el instalador.
/// </para>
/// </remarks>
public sealed class UpdateService : IDisposable
{
    /// <summary>Lo que espera tras arrancar antes de la primera búsqueda: que no compita con el arranque.</summary>
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(2);

    /// <summary>Cada cuánto vuelve a buscar.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromHours(24);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly Func<AppSettings> _settings;
    private readonly Action _save;
    private readonly Dispatcher _dispatcher;
    private readonly IAppLogger _logger;
    private readonly Action<string, string> _notify;
    private readonly Action _exit;
    private readonly Func<bool> _canRestartNow;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _applyTimer;
    private int _busy;

    /// <summary>La versión ya bajada y comprobada que espera a aplicarse, y dónde está.</summary>
    private (string Version, string Folder)? _ready;

    /// <param name="settings">Los ajustes, para leer <see cref="AppSettings.Updates"/>.</param>
    /// <param name="save">Guarda los ajustes.</param>
    /// <param name="dispatcher">El hilo de la interfaz.</param>
    /// <param name="notify">Un aviso en la bandeja: título y texto.</param>
    /// <param name="exit">Cierra Vdesk por las buenas, para que se apliquen los ficheros nuevos.</param>
    /// <param name="canRestartNow">
    /// Si se puede reiniciar sin molestar (<see cref="AutoUpdatePolicy.CanRestartNow"/>): sólo
    /// cuenta para la instalación sola. En el hilo de la interfaz.
    /// </param>
    /// <param name="logger">El diario.</param>
    public UpdateService(Func<AppSettings> settings, Action save, Dispatcher dispatcher, Action<string, string> notify, Action exit, Func<bool> canRestartNow, IAppLogger logger)
    {
        _settings = settings;
        _save = save;
        _dispatcher = dispatcher;
        _notify = notify;
        _exit = exit;
        _canRestartNow = canRestartNow;
        _logger = logger;

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = FirstCheck };
        _timer.Tick += async (_, _) =>
        {
            UpdateSettings ajustes = _settings().Updates;
            _timer.Interval = ajustes.InstallAutomatically ? AutoUpdatePolicy.CheckEvery : Every;

            if (ajustes.CheckAutomatically || ajustes.InstallAutomatically)
            {
                await CheckAsync(manual: false);
            }
        };
        _timer.Start();

        // Con una versión ya bajada esperando, se mira cada poco si es buen momento para aplicarla.
        _applyTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => ApplyIfIdle(), dispatcher);
        _applyTimer.Start();

        // Si la última vez se instaló sola, se dice ahora, ya con la versión nueva abierta.
        _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            UpdateSettings ajustes = _settings().Updates;

            if (ajustes.InstalledAutomatically.Length > 0)
            {
                if (ajustes.InstalledAutomatically == CurrentVersion)
                {
                    _notify(Loc.Format("Update.UpdatedTitle", CurrentVersion), Loc.Get("Update.UpdatedText"));
                }

                ajustes.InstalledAutomatically = "";
                _save();
            }
        });
    }

    /// <summary>Si la versión bajada se puede aplicar ya sin molestar, se aplica.</summary>
    private void ApplyIfIdle()
    {
        if (_ready is not { } listo || !_settings().Updates.InstallAutomatically || IsBusy || !_canRestartNow())
        {
            return;
        }

        if (Available?.Version != listo.Version || !Directory.Exists(listo.Folder))
        {
            _ready = null;
            return;
        }

        _ = InstallAsync(automatic: true);
    }

    /// <summary>La versión que lleva esta Vdesk.</summary>
    public string CurrentVersion => AppVersion.Display;

    /// <summary>La versión nueva que hay, ya comprobada; nula si no hay o no se ha mirado.</summary>
    public UpdateManifest? Available { get; private set; }

    /// <summary>Lo que se le dice al usuario de cómo está la cosa, ya traducido.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Si está buscando o bajando algo: para enseñar el «Cargando…».</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>Cambió <see cref="Available"/>, <see cref="Status"/> o <see cref="IsBusy"/>. En el hilo de la interfaz.</summary>
    public event EventHandler? Changed;

    /// <summary>Si esta Vdesk es la del instalador y se puede actualizar fichero a fichero.</summary>
    public static bool CanPatchInPlace =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "hostfxr.dll"))
        && AppContext.BaseDirectory.Contains(@"\Programs\", StringComparison.OrdinalIgnoreCase);

    private string Server => _settings().Updates.Server is { Length: > 0 } s ? s : UpdateKeys.DefaultServer;

    // ------------------------------------------------------------------ buscar

    /// <summary>Busca si hay versión nueva. Con <paramref name="manual"/>, avisa también si no la hay.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        bool instalarSola = false;

        try
        {
            SetStatus(Loc.Get("Update.Checking"));

            UpdateSettings ajustes = _settings().Updates;
            List<UpdateManifest> encontrados = new();

            foreach (string canal in ajustes.Beta ? new[] { "stable", "beta" } : new[] { "stable" })
            {
                if (await ReadManifestAsync(canal) is { } m)
                {
                    encontrados.Add(m);
                }
            }

            // El canal estable siempre está publicado. Si ni ése aparece, el servidor no es el que
            // debe (pasó el 2026-10-09: el dominio apuntaba a otra carpeta y todo daba 404), y decir
            // «tienes la última versión» sería mentir.
            if (encontrados.Count == 0)
            {
                throw new HttpRequestException(HttpRequestError.Unknown, "no manifest", statusCode: System.Net.HttpStatusCode.NotFound);
            }

            UpdateVersion actual = UpdateVersion.Parse(CurrentVersion) ?? new UpdateVersion(new Version(0, 0, 0), null);
            UpdateManifest? mejor = encontrados
                .Where(m => UpdateVersion.Parse(m.Version) is { } v && v.IsNewerThan(actual))
                .OrderByDescending(m => UpdateVersion.Parse(m.Version))
                .FirstOrDefault();

            Available = mejor;

            if (mejor is null)
            {
                SetStatus(Loc.Format("Update.UpToDate", CurrentVersion));
                _logger.Info("UpdateChecked", ("Current", CurrentVersion), ("Found", encontrados.Count));
                return;
            }

            SetStatus(Loc.Format("Update.Available", mejor.Version, CurrentVersion));
            _logger.Info("UpdateAvailable", ("Current", CurrentVersion), ("New", mejor.Version));

            if (ajustes.InstallAutomatically && CanPatchInPlace)
            {
                // Sin preguntar: se baja ya y se aplica cuando no moleste. El aviso, al terminar.
                instalarSola = true;
            }
            else if (manual || ajustes.LastAnnounced != mejor.Version)
            {
                ajustes.LastAnnounced = mejor.Version;
                _save();
                _notify(Loc.Format("Update.AvailableTitle", mejor.Version), Loc.Get("Update.AvailableHint"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException)
        {
            _logger.Warn("UpdateCheckFailed", ex.Message);
            SetStatus(Loc.Format("Update.CheckFailed", Explain(ex)));
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            RaiseChanged();
        }

        if (instalarSola)
        {
            await InstallAsync(automatic: true);
        }
    }

    /// <summary>El manifiesto de un canal, sólo si su firma vale; nulo si no hay.</summary>
    private async Task<UpdateManifest?> ReadManifestAsync(string canal)
    {
        using HttpResponseMessage json = await Http.GetAsync(UpdateKeys.UrlFor(Server, UpdateManifest.FileName(canal))).ConfigureAwait(false);

        if (json.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        json.EnsureSuccessStatusCode();
        byte[] bytes = await json.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        string firma = await Http.GetStringAsync(UpdateKeys.UrlFor(Server, UpdateManifest.SignatureName(canal))).ConfigureAwait(false);

        if (!UpdateSignature.Verify(bytes, firma, UpdateKeys.PublicKey))
        {
            // Firmado por otro, o a medio publicar: no se cree nada.
            _logger.Warn("UpdateSignatureInvalid", ("Channel", canal));
            throw new InvalidDataException(Loc.Get("Update.BadSignature"));
        }

        return UpdateManifest.Parse(bytes);
    }

    // ------------------------------------------------------------------ instalar

    /// <summary>
    /// Se baja lo que cambia y cierra Vdesk para ponerlo en su sitio; vuelve a abrirse sola. Si esta
    /// Vdesk no es la del instalador, abre la descarga del instalador.
    /// </summary>
    /// <param name="automatic">
    /// Por la instalación sola: si no es buen momento para reiniciar, deja lo bajado listo y
    /// espera (<see cref="ApplyIfIdle"/>).
    /// </param>
    public async Task InstallAsync(bool automatic = false)
    {
        if (Available is not { } nueva || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            if (!CanPatchInPlace)
            {
                if (!automatic)
                {
                    Process.Start(new ProcessStartInfo(UpdateKeys.UrlFor(Server, nueva.Setup.Path).ToString()) { UseShellExecute = true })?.Dispose();
                    SetStatus(Loc.Get("Update.DownloadingInstaller"));
                }

                return;
            }

            string app = AppContext.BaseDirectory;

            if (_ready is { } listo && listo.Version == nueva.Version && Directory.Exists(listo.Folder))
            {
                await RestartIntoAsync(nueva.Version, listo.Folder, app, automatic).ConfigureAwait(false);
                return;
            }

            IReadOnlyList<UpdateFile> faltan = UpdatePlan.FilesToDownload(nueva, ruta => LocalHash(app, ruta, nueva));

            string nuevos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VirtualDeskManager", "actualizacion", nueva.Version);

            if (Directory.Exists(nuevos))
            {
                Directory.Delete(nuevos, recursive: true);
            }

            long total = faltan.Sum(f => f.Size);
            long bajado = 0;

            foreach (UpdateFile f in faltan)
            {
                SetStatus(Loc.Format("Update.Downloading", nueva.Version, bajado * 100 / Math.Max(1, total)));

                byte[] contenido = await Http.GetByteArrayAsync(UpdateKeys.UrlFor(Server, UpdateManifest.BlobPath(f.Sha256))).ConfigureAwait(false);

                if (!string.Equals(Convert.ToHexString(SHA256.HashData(contenido)), f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(Loc.Format("Update.BadFile", f.Path));
                }

                string destino = Path.Combine(nuevos, f.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                await File.WriteAllBytesAsync(destino, contenido).ConfigureAwait(false);
                bajado += f.Size;
            }

            _logger.Info("UpdateDownloaded", ("Version", nueva.Version), ("Files", faltan.Count), ("Bytes", total));
            _ready = (nueva.Version, nuevos);

            await RestartIntoAsync(nueva.Version, nuevos, app, automatic).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn("UpdateInstallFailed", ex.Message);
            SetStatus(Loc.Format("Update.InstallFailed", Explain(ex)));
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            RaiseChanged();
        }
    }

    /// <summary>
    /// Cierra Vdesk para poner lo bajado en su sitio. Si es la instalación sola y el usuario está
    /// delante, o hay algo pasando por el enlace, no: se queda listo para luego, avisando una vez.
    /// </summary>
    private async Task RestartIntoAsync(string version, string nuevos, string app, bool automatic)
    {
        if (automatic)
        {
            bool ahora = await _dispatcher.InvokeAsync(_canRestartNow);

            if (!ahora)
            {
                SetStatus(Loc.Format("Update.ReadyWhenIdle", version));

                await _dispatcher.InvokeAsync(() =>
                {
                    UpdateSettings ajustes = _settings().Updates;

                    if (ajustes.LastAnnounced != version)
                    {
                        ajustes.LastAnnounced = version;
                        _save();
                        _notify(Loc.Format("Update.ReadyTitle", version), Loc.Get("Update.ReadyText"));
                    }
                });

                return;
            }

            await _dispatcher.InvokeAsync(() =>
            {
                _settings().Updates.InstalledAutomatically = version;
                _save();
            });
        }

        _logger.Info("UpdateRestarting", ("Version", version), ("Automatic", automatic));
        SetStatus(Loc.Format("Update.Restarting", version));

        Apply(nuevos, app);
        _ready = null;
        _dispatcher.Invoke(_exit);
    }

    /// <summary>
    /// La huella de un fichero de aquí, o nula si no está. Si el tamaño ya no coincide no hace falta
    /// leerlo: ha cambiado.
    /// </summary>
    private static string? LocalHash(string app, string ruta, UpdateManifest nueva)
    {
        string local = Path.Combine(app, ruta.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(local))
        {
            return null;
        }

        long esperado = nueva.Files.First(f => f.Path == ruta).Size;

        if (new FileInfo(local).Length != esperado)
        {
            return "";
        }

        using FileStream flujo = File.OpenRead(local);
        return Convert.ToHexString(SHA256.HashData(flujo));
    }

    /// <summary>
    /// Deja preparado el PowerShell que, cuando Vdesk se cierre, copia lo nuevo encima y lo vuelve a
    /// abrir. Sale en una ventana oculta y apunta lo que hace al lado.
    /// </summary>
    private void Apply(string nuevos, string app)
    {
        string exe = Path.Combine(app, "VirtualDeskManager.exe");
        string script = Path.Combine(Path.GetDirectoryName(nuevos)!, "aplicar.ps1");
        string registro = Path.Combine(Path.GetDirectoryName(nuevos)!, "aplicar.log");

        File.WriteAllText(script, """
            param([int]$Proceso, [string]$Origen, [string]$Destino, [string]$Exe, [string]$Registro)
            "$(Get-Date -Format s) esperando a que se cierre Virtual Desk Manager ($Proceso)" | Out-File $Registro -Encoding utf8
            Wait-Process -Id $Proceso -Timeout 120 -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
            robocopy $Origen $Destino /E /R:10 /W:1 /NP /NDL /NJH | Out-File $Registro -Append -Encoding utf8
            "$(Get-Date -Format s) robocopy: $LASTEXITCODE" | Out-File $Registro -Append -Encoding utf8
            if ($LASTEXITCODE -lt 8) { Remove-Item -LiteralPath $Origen -Recurse -Force -ErrorAction SilentlyContinue }
            Start-Process -FilePath $Exe
            """, new UTF8Encoding(true));

        ProcessStartInfo inicio = new("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                "-Proceso", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-Origen", nuevos.TrimEnd('\\'),
                "-Destino", app.TrimEnd('\\'),
                "-Exe", exe,
                "-Registro", registro
            }
        };

        Process.Start(inicio)?.Dispose();
        _logger.Info("UpdateApplying", ("From", nuevos), ("To", app));
    }

    private void SetStatus(string text)
    {
        Status = text;
        RaiseChanged();
    }

    /// <summary>
    /// El fallo en palabras de usuario. Los de red llegan de .NET en inglés técnico («The SSL
    /// connection could not be established…»), que no dice qué pasa ni de quién es la culpa; el
    /// detalle queda en el diario.
    /// </summary>
    private static string Explain(Exception ex) => ex switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => Loc.Get("Update.ErrorSecure"),
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError } => Loc.Get("Update.ErrorNoConnection"),
        HttpRequestException { StatusCode: { } codigo } => Loc.Format("Update.ErrorServer", (int)codigo),
        HttpRequestException or TaskCanceledException => Loc.Get("Update.ErrorNoConnection"),
        _ => ex.Message
    };

    private void RaiseChanged() => _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer.Stop();
        _applyTimer.Stop();
    }
}
