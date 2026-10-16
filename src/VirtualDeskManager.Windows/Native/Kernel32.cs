using System.Runtime.InteropServices;
using System.Text;

namespace VirtualDeskManager.Windows.Native;

/// <summary>Declaraciones de kernel32: procesos, Job Objects y modulos.</summary>
public static class Kernel32
{
    /// <summary>Asa de un modulo ya cargado en este proceso; con null, la del propio ejecutable.</summary>
    /// <remarks>
    /// Se usa con null para tener el <c>HINSTANCE</c> con el que registrar clases de ventana. El asa
    /// no se libera.
    /// </remarks>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    public static extern nint GetModuleHandle(string? moduleName);

    /// <summary>Abre un proceso por su identificador con los permisos pedidos.</summary>
    /// <remarks>
    /// Devuelve 0 si el proceso ya no existe o no hay permiso (procesos protegidos o de otro
    /// usuario). Con <see cref="NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION"/> se pueden abrir
    /// casi todos sin ser administrador. El asa se cierra con <see cref="CloseHandle"/>.
    /// </remarks>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    /// <summary>Cierra un asa del nucleo (proceso, Job Object, evento...).</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint handle);

    /// <summary>Crea un Job Object, con nombre o anonimo.</summary>
    /// <remarks>
    /// Devuelve 0 si falla. Cerrar la ultima asa de un job con <c>KILL_ON_JOB_CLOSE</c> mata a todos
    /// los procesos de dentro: por eso el job de los procesos hijos nunca se cierra a mano.
    /// </remarks>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateJobObjectW(nint attributes, string? name);

    /// <summary>Fija los limites de un Job Object.</summary>
    /// <remarks>
    /// <paramref name="infoClass"/> dice que estructura hay en <paramref name="info"/>; Vdesk usa la
    /// 9 (<c>JobObjectExtendedLimitInformation</c>) para activar <c>KILL_ON_JOB_CLOSE</c>.
    /// </remarks>
    /// <param name="job">Asa del job.</param>
    /// <param name="infoClass">Clase de informacion (<c>JOBOBJECTINFOCLASS</c>).</param>
    /// <param name="info">Puntero a la estructura que corresponde a esa clase.</param>
    /// <param name="length">Tamano en bytes de esa estructura.</param>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint length);

    /// <summary>Mete un proceso en un Job Object.</summary>
    /// <remarks>
    /// Puede fallar si el proceso ya esta en un job que no permite anidar, o si es una aplicacion
    /// empaquetada que se escapa del job al lanzarse.
    /// </remarks>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssignProcessToJobObject(nint job, nint process);

    /// <summary>El <c>AppUserModelID</c> de un proceso empaquetado.</summary>
    /// <remarks>
    /// Devuelve un codigo de error y no lo pone en GetLastError, asi que nada de SetLastError.
    /// Que un proceso no este empaquetado no es un fallo: responde 15703 y es lo normal en
    /// cualquier aplicacion de escritorio de las de siempre.
    /// </remarks>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder id);

    /// <summary>Ruta completa del ejecutable de un proceso abierto.</summary>
    /// <remarks>
    /// <paramref name="size"/> entra con la capacidad del <see cref="StringBuilder"/> en caracteres y
    /// sale con los caracteres escritos. Con <paramref name="flags"/> a 0 la ruta es de Win32
    /// (<c>C:\...</c>), no de dispositivo.
    /// </remarks>
    /// <param name="process">Asa del proceso, con al menos <c>PROCESS_QUERY_LIMITED_INFORMATION</c>.</param>
    /// <param name="flags">0 para ruta Win32; 1 (<c>PROCESS_NAME_NATIVE</c>) para ruta de dispositivo.</param>
    /// <param name="exeName">Donde se escribe la ruta.</param>
    /// <param name="size">Capacidad a la entrada; longitud escrita a la salida.</param>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder exeName, ref int size);

    /// <summary>
    /// Full path of the executable of a process, or an empty string when the process is gone or
    /// protected. Never throws (spec section 35).
    /// </summary>
    public static string GetProcessImagePath(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        nint handle = OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);

        if (handle == 0)
        {
            return string.Empty;
        }

        try
        {
            int capacity = 1024;
            StringBuilder builder = new(capacity);

            return QueryFullProcessImageName(handle, 0, builder, ref capacity)
                ? builder.ToString()
                : string.Empty;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
