using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>
/// Una aplicación abierta ahora mismo, tal y como la reconoce una persona.
/// </summary>
/// <remarks>
/// Existe porque pedirle a alguien que escriba <c>olk.exe</c> para hacer una regla de Outlook es
/// pedirle que sepa algo que no tiene por qué saber. Aquí se juntan el nombre que la aplicación
/// declara de sí misma, su icono y cuántas ventanas tiene, que es con lo que cualquiera la
/// identifica; el nombre del proceso queda debajo, como detalle.
/// </remarks>
public sealed class RunningApplication
{
    /// <summary>Junta lo que se sabe de una aplicación abierta.</summary>
    /// <param name="processName">El ejecutable con extensión.</param>
    /// <param name="friendlyName">El nombre que la aplicación se da a sí misma.</param>
    /// <param name="executablePath">La ruta completa del ejecutable.</param>
    /// <param name="windowCount">Cuántas ventanas tiene abiertas.</param>
    /// <param name="sampleTitle">El título de una de ellas.</param>
    /// <param name="icon">Su icono, si se pudo sacar.</param>
    public RunningApplication(
        string processName,
        string friendlyName,
        string executablePath,
        int windowCount,
        string sampleTitle,
        ImageSource? icon)
    {
        ProcessName = processName;
        FriendlyName = friendlyName;
        ExecutablePath = executablePath;
        WindowCount = windowCount;
        SampleTitle = sampleTitle;
        Icon = icon;
    }

    /// <summary>El ejecutable con extensión, que es lo que va en la regla.</summary>
    public string ProcessName { get; }

    /// <summary>El nombre que la aplicación se da a sí misma, p. ej. "Microsoft Outlook".</summary>
    public string FriendlyName { get; }

    /// <summary>La ruta completa del ejecutable; vacía si no se pudo saber.</summary>
    public string ExecutablePath { get; }

    /// <summary>Cuántas ventanas tiene abiertas ahora.</summary>
    public int WindowCount { get; }

    /// <summary>Título de una de sus ventanas, para desempatar entre aplicaciones parecidas.</summary>
    public string SampleTitle { get; }

    /// <summary>El icono del ejecutable, o <c>null</c> si no se pudo sacar.</summary>
    public ImageSource? Icon { get; }

    /// <summary>Lo que se busca al escribir en el cuadro de búsqueda.</summary>
    public bool Matches(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        text = text.Trim();

        return FriendlyName.Contains(text, StringComparison.CurrentCultureIgnoreCase)
               || ProcessName.Contains(text, StringComparison.OrdinalIgnoreCase)
               || SampleTitle.Contains(text, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>El nombre y, entre paréntesis, el ejecutable: «Microsoft Outlook (olk.exe)».</summary>
    public override string ToString() => $"{FriendlyName} ({ProcessName})";
}

/// <summary>Arma la lista de aplicaciones abiertas a partir de las ventanas que hay.</summary>
public static class RunningApplications
{
    /// <summary>
    /// Ventanas del sistema que no son aplicaciones para nadie.
    /// </summary>
    /// <remarks>
    /// El shell mantiene viva una ventana visible y con título de clase
    /// <c>Windows.UI.Core.CoreWindow</c> para la entrada de texto, y pasa todas las pruebas de
    /// ventana normal. En una lista para elegir aplicación no pinta nada.
    /// </remarks>
    private const string ShellInputClass = "Windows.UI.Core.CoreWindow";

    /// <summary>
    /// Procesos que alojan a otros y que no son una aplicación para nadie.
    /// </summary>
    /// <remarks>
    /// <c>ApplicationFrameHost</c> es el marco en el que Windows mete las aplicaciones de la
    /// Store, así que aparece con el título de la que tenga dentro —"Configuración", pongamos— y
    /// se colaba el primero en la lista por orden alfabético. Ofrecerlo sería una trampa: una
    /// regla sobre él no se aplica a la aplicación que se ve, sino a todas las de la Store a la
    /// vez. Quien de verdad lo quiera puede escribirlo en las condiciones finas.
    ///
    /// Las aplicaciones de la Store con proceso propio, como la Calculadora, no se ven afectadas:
    /// esas salen con su propio nombre.
    /// </remarks>
    private static readonly string[] HostProcesses =
    {
        "ApplicationFrameHost.exe",
        "TextInputHost.exe",
        "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe",
        "SearchHost.exe"
    };

    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Una entrada por aplicación, ordenadas por nombre.
    /// </summary>
    /// <param name="windows">De dónde salen las ventanas.</param>
    /// <param name="ownProcessId">El de esta aplicación, que se excluye.</param>
    public static IReadOnlyList<RunningApplication> Collect(IWindowManager windows, uint ownProcessId)
    {
        ArgumentNullException.ThrowIfNull(windows);

        Dictionary<string, List<WindowInfo>> byProcess = new(StringComparer.OrdinalIgnoreCase);

        foreach (WindowInfo window in windows.GetTopLevelWindows())
        {
            if (window.ProcessId == ownProcessId
                || window.ProcessName.Length == 0
                || string.Equals(window.ClassName, ShellInputClass, StringComparison.Ordinal)
                || HostProcesses.Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!byProcess.TryGetValue(window.ProcessName, out List<WindowInfo>? list))
            {
                list = new List<WindowInfo>();
                byProcess[window.ProcessName] = list;
            }

            list.Add(window);
        }

        List<RunningApplication> applications = new(byProcess.Count);

        foreach (KeyValuePair<string, List<WindowInfo>> pair in byProcess)
        {
            WindowInfo first = pair.Value[0];

            applications.Add(new RunningApplication(
                pair.Key,
                FriendlyName(first),
                first.ExecutablePath,
                pair.Value.Count,
                pair.Value.FirstOrDefault(w => w.Title.Length > 0)?.Title ?? "",
                LoadIcon(first.ExecutablePath)));
        }

        return applications
            .OrderBy(a => a.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// El nombre que la aplicación declara, y si no lo declara, el del ejecutable presentable.
    /// </summary>
    /// <remarks>
    /// Se prefiere la descripción del fichero porque es la que pone el fabricante y es la que
    /// sale en el Administrador de tareas: "Microsoft Outlook" y no "olk". Hay ejecutables que la
    /// dejan vacía o la rellenan con algo inútil, y para esos queda el nombre del fichero con la
    /// primera letra en mayúscula, que al menos se lee.
    /// </remarks>
    private static string FriendlyName(WindowInfo window)
    {
        try
        {
            if (window.ExecutablePath.Length > 0 && File.Exists(window.ExecutablePath))
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(window.ExecutablePath);

                string described = (info.FileDescription ?? "").Trim();

                if (described.Length > 0)
                {
                    return described;
                }

                string product = (info.ProductName ?? "").Trim();

                if (product.Length > 0)
                {
                    return product;
                }
            }
        }
        catch (Exception)
        {
            // Un ejecutable que no se deja leer no es motivo para dejar fuera la aplicación.
        }

        string name = Path.GetFileNameWithoutExtension(window.ProcessName);

        return name.Length switch
        {
            0 => window.ProcessName,
            1 => name.ToUpperInvariant(),
            _ => char.ToUpperInvariant(name[0]) + name[1..]
        };
    }

    /// <summary>
    /// El icono del ejecutable, cacheado por ruta.
    /// </summary>
    /// <remarks>
    /// Cacheado porque la lista se rehace cada vez que se abre el editor y extraer un icono toca
    /// disco. Los <see cref="ImageSource"/> se congelan para poder usarse desde cualquier hilo y
    /// para que WPF no tenga que vigilarlos.
    /// </remarks>
    private static ImageSource? LoadIcon(string executablePath)
    {
        if (executablePath.Length == 0)
        {
            return null;
        }

        if (IconCache.TryGetValue(executablePath, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? icon = null;

        try
        {
            using System.Drawing.Icon? extracted = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);

            if (extracted is not null)
            {
                icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    extracted.Handle,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                icon.Freeze();
            }
        }
        catch (Exception)
        {
            // Sin icono la fila sigue siendo perfectamente utilizable.
        }

        IconCache[executablePath] = icon;
        return icon;
    }
}
