namespace VirtualDeskManager.Core.Updates;

/// <summary>
/// Lo que Vdesk lleva dentro para actualizarse: dónde mirar y con qué clave comprobar la firma.
/// </summary>
public static class UpdateKeys
{
    /// <summary>
    /// La clave pública (ECDSA P-256, SubjectPublicKeyInfo en base64) con la que se comprueban los
    /// manifiestos. La privada la guarda quien publica, cifrada para su usuario de Windows; nunca va
    /// al repositorio.
    /// </summary>
    /// <remarks>
    /// Cambiarla deja a las Vdesk ya instaladas sin poder comprobar las versiones nuevas: sólo se
    /// cambia si la privada se pierde o se filtra, y entonces hay que repartir a mano la versión
    /// que traiga la nueva.
    /// </remarks>
    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEUZnktIaNjVuw8bxDLyC6Bmgg7x9lbL3Nv3qu/2v25OIQpZFMwtaOzpTCupiRWW6+Wdb7fX0QM73cRIG4j2b5rg==";

    /// <summary>
    /// La dirección web del servidor de actualizaciones, con su <c>d.php</c> (decidida por el
    /// usuario el 2026-10-09). Se puede cambiar en los ajustes (<c>Updates.Server</c>).
    /// </summary>
    public const string DefaultServer = "https://durillo.es/updates";

    /// <summary>
    /// La dirección de un fichero publicado: pasa siempre por <c>d.php</c>, con la ruta en base64url
    /// para que no se lea a simple vista (ver installer\servidor-actualizaciones).
    /// </summary>
    /// <param name="server">La raíz del servidor, como <c>https://ejemplo.com/vdesk</c>.</param>
    /// <param name="path">La ruta dentro de él, como <c>stable.json</c>.</param>
    public static Uri UrlFor(string server, string path)
    {
        string codificada = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return new Uri(server.TrimEnd('/') + "/d.php?f=" + codificada);
    }
}
