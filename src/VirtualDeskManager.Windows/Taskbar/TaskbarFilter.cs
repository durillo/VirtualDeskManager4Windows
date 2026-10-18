using Microsoft.Win32;

namespace VirtualDeskManager.Windows.Taskbar;

/// <summary>
/// Si la barra de tareas sólo enseña las ventanas del escritorio actual.
/// </summary>
/// <remarks>
/// Importa más de lo que parece, y se descubrió midiendo: con el filtro puesto —que es como viene
/// Windows 11 de fábrica— una aplicación abierta en otro escritorio <b>no tiene botón</b> en la
/// barra, así que no hay dónde ponerle el distintivo. La función sigue sirviendo, porque los
/// iconos anclados sí están siempre y ésos sí se marcan, pero enseña bastante menos de lo que el
/// usuario espera al encenderla.
///
/// Se lee para poder avisarlo en los ajustes en vez de dejar que parezca que no funciona. No se
/// cambia: el ajuste es del usuario y está en Configuración de Windows.
/// </remarks>
public static class TaskbarFilter
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private const string Value = "VirtualDesktopTaskbarFilter";

    /// <summary>
    /// Cierto cuando la barra oculta lo que está en otros escritorios.
    /// </summary>
    /// <remarks>
    /// Sin valor en el registro significa el de fábrica, que es filtrar. Si no se puede leer se
    /// dice que no filtra: un aviso de más sobre algo que no pasa confunde más que ayudar.
    /// </remarks>
    public static bool ShowsOnlyCurrentDesktop()
    {
        try
        {
            using RegistryKey? clave = Registry.CurrentUser.OpenSubKey(Key);

            if (clave?.GetValue(Value) is not int valor)
            {
                return true;
            }

            return valor != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
