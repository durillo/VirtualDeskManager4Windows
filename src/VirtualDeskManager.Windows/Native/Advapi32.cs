using System.Runtime.InteropServices;

namespace VirtualDeskManager.Windows.Native;

/// <summary>Lo justo del gestor de servicios para arrancar, parar y consultar un servicio.</summary>
public static class Advapi32
{
    /// <summary>Permiso para conectarse al gestor de servicios; basta para abrir un servicio despues.</summary>
    public const uint SC_MANAGER_CONNECT = 0x0001;

    /// <summary>Permiso para consultar el estado de un servicio con <see cref="QueryServiceStatus"/>.</summary>
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    /// <summary>Permiso para arrancar un servicio con <see cref="StartService"/>.</summary>
    public const uint SERVICE_START = 0x0010;
    /// <summary>Permiso para mandar a un servicio la orden de parar.</summary>
    public const uint SERVICE_STOP = 0x0020;

    /// <summary>Orden de <see cref="ControlService"/> que pide al servicio que se pare.</summary>
    public const uint SERVICE_CONTROL_STOP = 0x00000001;

    /// <summary>Estado del servicio: parado.</summary>
    public const uint SERVICE_STOPPED = 0x00000001;
    /// <summary>Estado del servicio: arrancando, todavia sin terminar.</summary>
    public const uint SERVICE_START_PENDING = 0x00000002;
    /// <summary>Estado del servicio: parandose, todavia sin terminar.</summary>
    public const uint SERVICE_STOP_PENDING = 0x00000003;
    /// <summary>Estado del servicio: en marcha.</summary>
    public const uint SERVICE_RUNNING = 0x00000004;

    /// <summary>Error 5: acceso denegado. Con un usuario normal es lo que responde arrancar o parar un servicio del sistema.</summary>
    public const int ERROR_ACCESS_DENIED = 5;
    /// <summary>Error 1060: el servicio no esta instalado.</summary>
    public const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    /// <summary>Error 1056: el servicio ya estaba en marcha. Al arrancarlo no se trata como fallo.</summary>
    public const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    /// <summary>Error 1062: el servicio no estaba en marcha. Al pararlo no se trata como fallo.</summary>
    public const int ERROR_SERVICE_NOT_ACTIVE = 1062;

    /// <summary>Estado de un servicio tal como lo devuelve el gestor de servicios.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS
    {
        /// <summary>Tipo de servicio (proceso propio, compartido, controlador...).</summary>
        public uint ServiceType;
        /// <summary>Estado actual: <see cref="SERVICE_STOPPED"/>, <see cref="SERVICE_RUNNING"/>, etc.</summary>
        public uint CurrentState;
        /// <summary>Ordenes de control que el servicio acepta ahora mismo.</summary>
        public uint ControlsAccepted;
        /// <summary>Codigo de error Win32 con el que el servicio informa de un fallo al arrancar o parar.</summary>
        public uint Win32ExitCode;
        /// <summary>Codigo de error propio del servicio, cuando <see cref="Win32ExitCode"/> lo indica.</summary>
        public uint ServiceSpecificExitCode;
        /// <summary>Contador que el servicio va subiendo durante una operacion larga para indicar progreso.</summary>
        public uint CheckPoint;
        /// <summary>Milisegundos que el servicio calcula que le faltan para el siguiente paso de una operacion pendiente.</summary>
        public uint WaitHint;
    }

    /// <summary>Abre una conexion con el gestor de servicios.</summary>
    /// <remarks>Devuelve 0 si falla; la causa esta en <c>Marshal.GetLastWin32Error</c>. El asa se cierra con <see cref="CloseServiceHandle"/>.</remarks>
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
    public static extern nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    /// <summary>Abre un servicio por su nombre con los permisos pedidos.</summary>
    /// <remarks>
    /// Devuelve 0 si falla; <see cref="ERROR_SERVICE_DOES_NOT_EXIST"/> en GetLastError significa que
    /// no esta instalado. El asa se cierra con <see cref="CloseServiceHandle"/>.
    /// </remarks>
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    public static extern nint OpenService(nint manager, string serviceName, uint desiredAccess);

    /// <summary>Arranca un servicio. No espera a que termine de arrancar.</summary>
    /// <remarks>
    /// Que devuelva true solo dice que se ha pedido; hay que consultar el estado con
    /// <see cref="QueryServiceStatus"/> hasta ver <see cref="SERVICE_RUNNING"/>.
    /// </remarks>
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "StartServiceW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool StartService(nint service, uint argumentCount, nint arguments);

    /// <summary>Manda una orden de control a un servicio, por ejemplo <see cref="SERVICE_CONTROL_STOP"/>.</summary>
    /// <remarks>Como con <see cref="StartService"/>, no espera a que el servicio llegue al estado final.</remarks>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ControlService(nint service, uint control, out SERVICE_STATUS status);

    /// <summary>Lee el estado actual de un servicio.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryServiceStatus(nint service, out SERVICE_STATUS status);

    /// <summary>Cierra un asa del gestor de servicios o de un servicio.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseServiceHandle(nint handle);
}
