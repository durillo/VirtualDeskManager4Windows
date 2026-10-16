using System.Runtime.InteropServices;

namespace VirtualDeskManager.Windows.Native;

/// <summary>
/// Lo justo del shell para preguntarle a una ventana qué aplicación dice ser.
/// </summary>
/// <remarks>
/// <c>IPropertyStore</c> se declara aquí a mano en vez de traer la interoperabilidad del shell
/// entera: sólo se usa <c>GetValue</c>, y el resto de métodos está sólo para que la tabla virtual
/// cuadre. El orden de esos métodos <b>es</b> el contrato — cambiarlo no da error de compilación,
/// da corrupción de memoria.
/// </remarks>
public static class Shell32
{
    /// <summary>Identificador de la interfaz <see cref="IPropertyStore"/>, para pedirla por <c>ref Guid</c>.</summary>
    public static readonly Guid IID_IPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    /// <summary><c>System.AppUserModel.ID</c>.</summary>
    public static readonly PropertyKey PKEY_AppUserModel_ID =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    /// <summary><c>System.AppUserModel.RelaunchCommand</c>: a qué ejecutable apunta un acceso directo.</summary>
    public static readonly PropertyKey PKEY_Link_TargetParsingPath =
        new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);

    /// <summary>
    /// El almacén de propiedades de un fichero, con el sistema de propiedades entero detrás.
    /// </summary>
    /// <remarks>
    /// No es lo mismo que pedirle su almacén al objeto enlace, y la diferencia costó un rato
    /// encontrarla: el del enlace sólo tiene lo que alguien guardó ahí a mano, y el acceso directo
    /// de Visual Studio Code <b>no</b> guarda su identificador. Éste pasa por los controladores de
    /// propiedades del shell, que es de donde sale el identificador derivado — el mismo que acaba
    /// usando la barra de tareas para agrupar.
    /// </remarks>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetPropertyStoreFromParsingName(
        string path,
        nint bindContext,
        uint flags,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);

    /// <summary>
    /// El almacén de propiedades de una ventana, donde una aplicación puede haber dejado su
    /// <c>AppUserModelID</c> (<see cref="PKEY_AppUserModel_ID"/>).
    /// </summary>
    /// <remarks>
    /// Devuelve un HRESULT (0 si va bien). Una propiedad que la ventana no tenga se lee como valor
    /// vacío con <see cref="IPropertyStore.GetValue"/>, no como error.
    /// </remarks>
    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(
        nint hwnd,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);

    /// <summary>Clave de una propiedad del shell (<c>PROPERTYKEY</c>): conjunto de propiedades más número.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        /// <summary>Identificador del conjunto de propiedades (<c>fmtid</c>).</summary>
        public Guid FormatId;
        /// <summary>Número de la propiedad dentro de ese conjunto (<c>pid</c>).</summary>
        public uint PropertyId;

        /// <summary>Crea la clave a partir de su conjunto y su número.</summary>
        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    /// <summary>
    /// <c>IPropertyStore</c> de COM: lectura y escritura de propiedades por <see cref="PropertyKey"/>.
    /// </summary>
    /// <remarks>Los métodos están en el orden de la tabla virtual; no se pueden reordenar.</remarks>
    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        /// <summary>Cuántas propiedades tiene el almacén.</summary>
        int GetCount(out uint count);

        /// <summary>La clave de la propiedad que ocupa la posición <paramref name="index"/>.</summary>
        int GetAt(uint index, out PropertyKey key);

        /// <summary>
        /// Lee el valor de una propiedad. Si no existe, devuelve S_OK con el valor vacío
        /// (<c>VT_EMPTY</c>).
        /// </summary>
        int GetValue(ref PropertyKey key, [In, Out] PropVariant value);

        /// <summary>Cambia el valor de una propiedad; no se guarda hasta <see cref="Commit"/>.</summary>
        int SetValue(ref PropertyKey key, PropVariant value);

        /// <summary>Guarda los cambios hechos con <see cref="SetValue"/>.</summary>
        int Commit();
    }

    /// <summary>Un <c>PROPVARIANT</c> del que sólo interesa el caso de cadena.</summary>
    /// <remarks>
    /// Se libera siempre con <c>PropVariantClear</c> aunque el valor no sea una cadena: si lo es,
    /// dentro hay memoria que reservó el shell, y darla por perdida en cada consulta es una fuga
    /// lenta en algo que se consulta por cada ventana y muchas veces al minuto.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public sealed class PropVariant : IDisposable
    {
        private ushort _type;
        private readonly ushort _reserved1;
        private readonly ushort _reserved2;
        private readonly ushort _reserved3;
        private nint _pointer;
        private readonly int _padding;

        /// <summary><c>VT_LPWSTR</c>.</summary>
        private const ushort StringType = 31;

        /// <summary>El valor si es una cadena (<c>VT_LPWSTR</c>); null si es de otro tipo o está vacío.</summary>
        public string? AsString() =>
            _type == StringType && _pointer != 0 ? Marshal.PtrToStringUni(_pointer) : null;

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear([In, Out] PropVariant value);

        /// <summary>Libera con <c>PropVariantClear</c> lo que el shell haya reservado dentro y deja el valor vacío.</summary>
        public void Dispose()
        {
            if (_type != 0)
            {
                PropVariantClear(this);
                _type = 0;
                _pointer = 0;
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>Red de seguridad por si nadie llamó a <see cref="Dispose"/>.</summary>
        ~PropVariant() => Dispose();
    }

    /// <summary>La carpeta de Descargas (<c>FOLDERID_Downloads</c>).</summary>
    public static readonly Guid FolderDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>
    /// La ruta de una carpeta conocida, aunque el usuario la haya movido a otro disco; nula si no se
    /// puede saber.
    /// </summary>
    public static string? KnownFolderPath(Guid folder)
    {
        if (SHGetKnownFolderPath(folder, 0, 0, out nint ruta) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(ruta);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ruta);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folder, uint flags, nint token, out nint path);
}
