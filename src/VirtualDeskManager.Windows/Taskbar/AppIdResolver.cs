using System.Runtime.InteropServices;
using System.Text;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Taskbar;

/// <summary>
/// A qué aplicación de la barra de tareas pertenece una ventana.
/// </summary>
/// <remarks>
/// Es la pieza difícil de todo esto, y se midió antes de escribirla. Lo evidente —preguntarle a
/// la ventana su <c>AppUserModelID</c> con <c>SHGetPropertyStoreForWindow</c>— <b>no basta</b>:
/// de catorce ventanas abiertas en este equipo sólo tres lo declaraban. Code, Teams, Spotify,
/// Outlook y Excel devolvían vacío aunque la barra las agrupa perfectamente, porque Windows
/// resuelve el identificador por dentro con un resolutor que no expone.
///
/// Así que hay tres capas, de la más fiable a la más aproximada, y ninguna inventa:
///
/// <list type="number">
/// <item>El que declara la ventana. Es el único que distingue los <b>perfiles de Edge</b>, que en
/// la barra son botones distintos (<c>MSEdge</c> y <c>MSEdge.UserData.Profile1</c>).</item>
/// <item><c>GetApplicationUserModelId</c> sobre el proceso. Exacto para aplicaciones empaquetadas
/// —Spotify, Teams, Outlook— y documentado desde Windows 8.</item>
/// <item>Los accesos directos del menú Inicio: cada uno lleva su identificador y a qué ejecutable
/// apunta, así que se le da la vuelta y se busca por ruta del proceso.</item>
/// </list>
///
/// Si ninguna responde, la ventana se queda sin aplicación y su botón sin distintivo. Es a
/// propósito: marcar el botón equivocado es peor que no marcar ninguno.
/// </remarks>
public sealed class AppIdResolver
{
    /// <summary>El proceso no está empaquetado, así que no tiene identificador propio.</summary>
    private const int NoApplication = 15703;

    private const int NoPackage = 15700;

    private readonly IAppLogger _logger;

    /// <summary>Ruta del ejecutable al identificador, según el menú Inicio.</summary>
    private Dictionary<string, string> _porRuta = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lo ya resuelto por proceso, que no cambia mientras el proceso viva.</summary>
    private readonly Dictionary<uint, string?> _cache = new();

    private DateTime _leidoElMenu = DateTime.MinValue;

    /// <summary>Crea el resolutor con la caché vacía; el menú Inicio se lee la primera vez que hace falta.</summary>
    public AppIdResolver(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <summary>Cada cuánto se vuelve a leer el menú Inicio, que cambia al instalar algo.</summary>
    public static readonly TimeSpan StartMenuLifetime = TimeSpan.FromMinutes(10);

    /// <summary>El identificador de aplicación de esta ventana, o nulo si no se puede saber.</summary>
    public string? Resolve(nint hwnd, uint processId, string? processPath)
    {
        // Primero el de la ventana, y no por orden de facilidad: es el unico que separa los
        // perfiles de Edge, que para la barra de tareas son aplicaciones distintas.
        string? declarado = FromWindow(hwnd);

        if (!string.IsNullOrEmpty(declarado))
        {
            return declarado;
        }

        if (_cache.TryGetValue(processId, out string? recordado))
        {
            return recordado;
        }

        string? resuelto = FromPackagedProcess(processId) ?? FromStartMenu(processPath);

        _cache[processId] = resuelto;

        return resuelto;
    }

    /// <summary>Olvida lo aprendido de procesos que ya no existen.</summary>
    public void Forget(IReadOnlyCollection<uint> alive)
    {
        ArgumentNullException.ThrowIfNull(alive);

        foreach (uint id in _cache.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _cache.Remove(id);
        }
    }

    // ------------------------------------------------------------------ capa 1

    private string? FromWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        try
        {
            Guid iid = Shell32.IID_IPropertyStore;

            if (Shell32.SHGetPropertyStoreForWindow(hwnd, ref iid, out Shell32.IPropertyStore? store) != 0
                || store is null)
            {
                return null;
            }

            try
            {
                Shell32.PropertyKey key = Shell32.PKEY_AppUserModel_ID;
                using Shell32.PropVariant value = new();

                if (store.GetValue(ref key, value) != 0)
                {
                    return null;
                }

                return value.AsString();
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("TaskbarAppIdFailed", ex.Message);
            return null;
        }
    }

    // ------------------------------------------------------------------ capa 2

    private string? FromPackagedProcess(uint processId)
    {
        nint proceso = Kernel32.OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

        if (proceso == 0)
        {
            return null;
        }

        try
        {
            uint largo = 512;
            StringBuilder texto = new((int)largo);

            int resultado = Kernel32.GetApplicationUserModelId(proceso, ref largo, texto);

            // No estar empaquetado es lo normal, no un fallo: pasa con toda aplicacion de
            // escritorio de las de siempre, y para esas esta la capa siguiente.
            if (resultado is NoApplication or NoPackage)
            {
                return null;
            }

            return resultado == 0 ? texto.ToString() : null;
        }
        finally
        {
            Kernel32.CloseHandle(proceso);
        }
    }

    // ------------------------------------------------------------------ capa 3

    private string? FromStartMenu(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        EnsureStartMenu();

        return _porRuta.TryGetValue(processPath, out string? id) ? id : null;
    }

    private void EnsureStartMenu()
    {
        if (DateTime.UtcNow - _leidoElMenu < StartMenuLifetime && _porRuta.Count > 0)
        {
            return;
        }

        _leidoElMenu = DateTime.UtcNow;

        try
        {
            _porRuta = ShellLinks.ReadStartMenu(_logger);
            _logger.Info("TaskbarStartMenuRead", ("Shortcuts", _porRuta.Count));
        }
        catch (Exception ex)
        {
            _logger.Warn("TaskbarStartMenuFailed", ex.Message);
        }
    }
}
