using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Taskbar;

/// <summary>
/// Lee del menú Inicio qué identificador de aplicación corresponde a cada ejecutable.
/// </summary>
/// <remarks>
/// Es la última capa del resolutor y la única que cubre las aplicaciones de escritorio de
/// siempre, que no son un paquete y no declaran nada. Cada acceso directo del menú Inicio sabe
/// con qué <c>AppUserModelID</c> va a agrupar la barra de tareas sus ventanas, y a qué ejecutable
/// apunta. Se lee esa pareja y se le da la vuelta.
///
/// Las dos cosas salen del <b>almacén de propiedades del fichero</b>, y no del objeto enlace.
/// Parece lo mismo y no lo es: el del enlace sólo devuelve lo que alguien guardó ahí
/// explícitamente, y por eso el acceso directo de Visual Studio Code —el editor donde se está
/// escribiendo esto— salía sin identificador. El del fichero pasa por los controladores de
/// propiedades del shell y devuelve también el derivado, que es el que la barra usa de verdad.
///
/// No cubre todo, y no pasa nada: una aplicación sin acceso directo se queda sin identificador y
/// su botón sin distintivo, que es mejor que marcar el botón de otra.
/// </remarks>
internal static class ShellLinks
{
    /// <summary><c>GPS_DEFAULT</c>: el sistema de propiedades completo.</summary>
    private const uint DefaultFlags = 0;

    /// <summary>
    /// Lee el menú Inicio en un hilo propio con apartamento STA.
    /// </summary>
    /// <remarks>
    /// Los objetos del shell son de apartamento, y quien llama aquí es un servicio en segundo
    /// plano al que no se le puede imponer el suyo.
    /// </remarks>
    public static Dictionary<string, string> ReadStartMenu(IAppLogger logger)
    {
        Dictionary<string, string> resultado = new(StringComparer.OrdinalIgnoreCase);

        Thread hilo = new(() => resultado = ReadHere(logger))
        {
            IsBackground = true,
            Name = "Vdesk menu Inicio"
        };

        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();

        return resultado;
    }

    private static Dictionary<string, string> ReadHere(IAppLogger logger)
    {
        Dictionary<string, string> porRuta = new(StringComparer.OrdinalIgnoreCase);

        string[] raices =
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Microsoft\Windows\Start Menu\Programs")
        };

        foreach (string raiz in raices)
        {
            if (!Directory.Exists(raiz))
            {
                continue;
            }

            foreach (string atajo in Enumerate(raiz, logger))
            {
                Read(atajo, porRuta);
            }
        }

        return porRuta;
    }

    private static IEnumerable<string> Enumerate(string root, IAppLogger logger)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            logger.Warn("TaskbarStartMenuFailed", ex.Message);
            return Array.Empty<string>();
        }
    }

    private static void Read(string path, Dictionary<string, string> into)
    {
        Shell32.IPropertyStore? store = null;

        try
        {
            Guid iid = Shell32.IID_IPropertyStore;

            if (Shell32.SHGetPropertyStoreFromParsingName(path, nint.Zero, DefaultFlags, ref iid, out store) != 0
                || store is null)
            {
                return;
            }

            string? id = Text(store, Shell32.PKEY_AppUserModel_ID);
            string? destino = Text(store, Shell32.PKEY_Link_TargetParsingPath);

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(destino))
            {
                return;
            }

            // El primero gana. Muchas aplicaciones dejan varios accesos directos al mismo
            // ejecutable y solo uno lleva el identificador que usa la barra; quedarse con el
            // ultimo iria cambiando entre arranques segun el orden del sistema de ficheros.
            _ = into.TryAdd(destino, id);
        }
        catch (COMException)
        {
            // Un acceso directo roto no es noticia, y hay cientos.
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    private static string? Text(Shell32.IPropertyStore store, Shell32.PropertyKey key)
    {
        using Shell32.PropVariant valor = new();

        return store.GetValue(ref key, valor) == 0 ? valor.AsString() : null;
    }
}
