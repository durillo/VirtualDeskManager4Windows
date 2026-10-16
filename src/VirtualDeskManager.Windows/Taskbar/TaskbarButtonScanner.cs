using System.Windows.Automation;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Windows.Taskbar;

/// <summary>Un botón de aplicación de la barra de tareas.</summary>
/// <param name="AppId">Su <c>AppUserModelID</c>, que es lo que identifica a la aplicación.</param>
/// <param name="Bounds">Dónde está en pantalla, en píxeles físicos.</param>
public sealed record TaskbarButton(string AppId, WindowBounds Bounds);

/// <summary>
/// Localiza los botones de la barra de tareas de Windows 11.
/// </summary>
/// <remarks>
/// Por UI Automation y no inyectando nada en el Explorador, que la sección 36 prohíbe: la barra
/// es suya, se dibuja con XAML y no expone ventanas por botón que se puedan subclasear. UIA es
/// una interfaz pública, funciona como usuario normal y no pide permisos.
///
/// La identificación va por el <c>AutomationId</c>, que llega como <c>"Appid: MSEdge"</c>. El
/// nombre <b>no</b> sirve: viene traducido —<i>"Spotify: 1 ventana en ejecución"</i>— y cambiaría
/// con el idioma del sistema.
///
/// Sólo se cuentan los botones de aplicación. La barra mezcla en el mismo sitio el botón de
/// Inicio, la bandeja, el reloj y el de mostrar escritorio, y todos ellos son botones para UIA.
/// </remarks>
public sealed class TaskbarButtonScanner
{
    /// <summary>La clase de los botones de aplicación, y sólo de ésos.</summary>
    private const string ButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    /// <summary>Lo que precede al identificador dentro del <c>AutomationId</c>.</summary>
    private const string Prefix = "Appid:";

    private readonly IAppLogger _logger;

    /// <summary>Crea el lector. No guarda nada: cada <see cref="Scan"/> recorre la barra de nuevo.</summary>
    public TaskbarButtonScanner(IAppLogger? logger = null) => _logger = logger ?? NullAppLogger.Instance;

    /// <summary>Los botones de aplicación de todas las barras de tareas, vacío si no se puede.</summary>
    /// <remarks>
    /// Devolver una lista vacía y no lanzar es deliberado: esto se consulta a menudo y desde un
    /// temporizador, y la barra puede estar reiniciándose —al cambiar de resolución, al reiniciar
    /// el Explorador— justo cuando se mira. Un hueco de un segundo no es un error.
    /// </remarks>
    public IReadOnlyList<TaskbarButton> Scan()
    {
        try
        {
            List<TaskbarButton> botones = new();

            // Con varios monitores hay una barra por pantalla: Shell_TrayWnd es la principal y
            // Shell_SecondaryTrayWnd las demas, y las dos llevan botones que marcar.
            foreach (string clase in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                foreach (AutomationElement barra in Find(AutomationElement.RootElement, TreeScope.Children,
                             AutomationElement.ClassNameProperty, clase))
                {
                    Collect(barra, botones);
                }
            }

            return botones;
        }
        catch (Exception ex)
        {
            _logger.Warn("TaskbarScanFailed", ex.Message);
            return Array.Empty<TaskbarButton>();
        }
    }

    private void Collect(AutomationElement bar, List<TaskbarButton> into)
    {
        foreach (AutomationElement boton in Find(bar, TreeScope.Descendants,
                     AutomationElement.ClassNameProperty, ButtonClass))
        {
            AutomationElement.AutomationElementInformation info;

            try
            {
                info = boton.Current;
            }
            catch (ElementNotAvailableException)
            {
                // El boton se ha ido mientras se recorria la lista, que pasa con solo cerrar una
                // aplicacion durante el barrido.
                continue;
            }

            string? appId = AppIdOf(info.AutomationId);

            if (appId is null)
            {
                continue;
            }

            System.Windows.Rect r = info.BoundingRectangle;

            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0)
            {
                continue;
            }

            into.Add(new TaskbarButton(
                appId,
                new WindowBounds((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height)));
        }
    }

    /// <summary>Saca el identificador de <c>"Appid: MSEdge"</c>.</summary>
    internal static string? AppIdOf(string? automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return null;
        }

        int corte = automationId.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);

        if (corte < 0)
        {
            return null;
        }

        string resto = automationId[(corte + Prefix.Length)..].Trim();

        return resto.Length == 0 ? null : resto;
    }

    private static IEnumerable<AutomationElement> Find(
        AutomationElement root,
        TreeScope scope,
        AutomationProperty property,
        string value)
    {
        AutomationElementCollection encontrados;

        try
        {
            encontrados = root.FindAll(scope, new PropertyCondition(property, value));
        }
        catch (ElementNotAvailableException)
        {
            yield break;
        }

        foreach (AutomationElement elemento in encontrados)
        {
            yield return elemento;
        }
    }
}
