using VirtualDeskManager.Core.Diagnostics;

namespace VirtualDeskManager.Windows.Shell;

/// <summary>
/// Limpia las entradas de «Enviar a» del Explorador que dejaban las versiones con enlace entre
/// equipos («Portátil (Vdesk)»): ese enlace es ahora el proyecto MiRed y aquí ya no hacen nada.
/// </summary>
/// <remarks>
/// Eran accesos directos en la carpeta SendTo del usuario. Sólo se tocan los que acaban en
/// <see cref="Suffix"/>, que son los que creaba esta aplicación.
/// </remarks>
public static class SendToShortcuts
{
    /// <summary>Lo que llevaban detrás del nombre del equipo; así se reconocen.</summary>
    public const string Suffix = " (Vdesk).lnk";

    /// <summary>Borra las entradas viejas, si queda alguna.</summary>
    /// <param name="logger">Dónde apuntar lo que falle.</param>
    public static void RemoveAll(IAppLogger logger)
    {
        string carpeta = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

        if (carpeta.Length == 0 || !Directory.Exists(carpeta))
        {
            return;
        }

        foreach (string sobra in Directory.EnumerateFiles(carpeta, "*" + Suffix))
        {
            try
            {
                File.Delete(sobra);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Warn("SendToShortcutFailed", ex.Message, ("File", Path.GetFileName(sobra)));
            }
        }
    }
}
