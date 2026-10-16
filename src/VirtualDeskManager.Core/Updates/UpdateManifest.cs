using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualDeskManager.Core.Updates;

/// <summary>Un fichero de una versión: dónde va, su huella y su tamaño.</summary>
/// <param name="Path">Relativo a la carpeta de la aplicación, con «/»: <c>runtimes/win-x64/native/x.dll</c>.</param>
/// <param name="Sha256">Su huella SHA-256 en hexadecimal; en el servidor se guarda con ese nombre.</param>
/// <param name="Size">Su tamaño en bytes.</param>
public sealed record UpdateFile(string Path, string Sha256, long Size);

/// <summary>
/// Lo que el servidor de actualizaciones dice de la última versión de un canal.
/// </summary>
/// <remarks>
/// <para>
/// Viaja como <c>&lt;canal&gt;.json</c> con su firma al lado (<c>&lt;canal&gt;.json.sig</c>). Vdesk
/// no se cree nada de lo que dice hasta comprobar la firma con la clave pública que lleva dentro
/// (<see cref="UpdateSignature"/>): quien entre en el servidor puede cambiar los ficheros, pero no
/// firmarlos.
/// </para>
/// <para>
/// Lleva dos maneras de llegar a la versión: el instalador completo, para quien no tiene Vdesk, y
/// la lista de ficheros con sus huellas, para que quien ya lo tiene se baje sólo los que cambian
/// (<see cref="UpdatePlan"/>). Los ficheros se guardan en el servidor por su huella, así que una
/// DLL que no cambia entre versiones está una sola vez.
/// </para>
/// </remarks>
/// <param name="Version">La versión, como <c>0.24.0</c> o <c>0.24.0-beta.2</c>.</param>
/// <param name="Channel">El canal: <c>stable</c> o <c>beta</c>.</param>
/// <param name="Published">Cuándo se publicó, en UTC.</param>
/// <param name="Notes">Las notas de la versión, en Markdown, para enseñarlas antes de actualizar.</param>
/// <param name="Setup">El instalador completo, relativo a la raíz del servidor.</param>
/// <param name="Files">Todos los ficheros de la aplicación en esa versión.</param>
public sealed record UpdateManifest(
    string Version,
    string Channel,
    DateTime Published,
    string Notes,
    UpdateFile Setup,
    IReadOnlyList<UpdateFile> Files)
{
    /// <summary>El nombre del manifiesto de un canal en el servidor.</summary>
    public static string FileName(string channel) => channel + ".json";

    /// <summary>El de su firma, al lado.</summary>
    public static string SignatureName(string channel) => channel + ".json.sig";

    /// <summary>Dónde está en el servidor el contenido de un fichero, por su huella.</summary>
    public static string BlobPath(string sha256) => "ficheros/" + sha256.ToLowerInvariant();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Lo pasa a JSON: son estos bytes los que se firman y se suben.</summary>
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Lee uno ya comprobado; lanza si no es un manifiesto.</summary>
    public static UpdateManifest Parse(byte[] json) =>
        JsonSerializer.Deserialize<UpdateManifest>(json, Json)
        ?? throw new InvalidDataException("El manifiesto está vacío.");
}
