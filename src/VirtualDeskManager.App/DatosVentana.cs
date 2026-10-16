using System;
using System.Collections.Generic;
using System.Linq;
using VirtualDeskManager.App.ViewModels;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App;

/// <summary>
/// Lo que ve y hace la ventana principal, sacado de la aplicación de verdad: los escritorios y sus
/// ventanas de <see cref="AppHost"/>.
/// </summary>
internal sealed class DatosVentana : IDatosVentana
{
    private readonly AppHost _host;
    private readonly Action<string?> _ajustes;
    private readonly object? _modeloAjustes;
    private readonly AccionesEscritorios _acciones;

    public DatosVentana(AppHost host, Action<string?> ajustes, object? modeloAjustes = null)
    {
        _host = host;
        _ajustes = ajustes;
        _modeloAjustes = modeloAjustes;
        _acciones = new AccionesEscritorios(host);
    }

    public void Save() => _host.Configuration.SaveSettings();

    public void OpenSettings(string? section) => _ajustes(section);

    public bool Dark
    {
        get => _host.Settings.VdeskDark;
        set => _host.Settings.VdeskDark = value;
    }

    public object? Settings => _modeloAjustes;

    public IReadOnlyList<EscritorioDatos> Desktops()
    {
        IReadOnlyList<VirtualDesktopInfo> escritorios = _host.VirtualDesktops.GetDesktops();
        Guid? actual = _host.VirtualDesktops.GetCurrentDesktop()?.Id;
        Dictionary<Guid, List<VentanaEnEscritorio>> ventanas = escritorios.ToDictionary(e => e.Id, _ => new List<VentanaEnEscritorio>());

        // Todas las pantallas juntas, para poner cada ventana en su sitio de la miniatura.
        System.Drawing.Rectangle pantalla = System.Windows.Forms.SystemInformation.VirtualScreen;
        double ancho = Math.Max(1, pantalla.Width), alto = Math.Max(1, pantalla.Height);
        int propio = Environment.ProcessId;

        foreach (WindowInfo v in _host.WindowManager.GetTopLevelWindows())
        {
            // Esta misma ventana no, ni las de la interfaz de Windows (como la vista de escritorios).
            if (v.ProcessId == propio || string.Equals(v.ClassName, "Windows.UI.Core.CoreWindow", StringComparison.Ordinal))
            {
                continue;
            }

            if (_host.VirtualDesktops.GetWindowDesktopId(v.Handle) is not { } donde || !ventanas.TryGetValue(donde, out List<VentanaEnEscritorio>? lista))
            {
                continue; // En todos los escritorios, o no se sabe.
            }

            WindowBounds b = v.Bounds;
            lista.Add(new VentanaEnEscritorio(
                v.Handle,
                v.ProcessName,
                v.Title,
                (b.X - pantalla.X) / ancho,
                (b.Y - pantalla.Y) / alto,
                b.Width / ancho,
                b.Height / alto,
                v.IsMinimized || b.Width <= 0));
        }

        return escritorios
            .Select(e => new EscritorioDatos(e.Id, e.Number, e.Name, e.Id == actual, ventanas[e.Id]))
            .ToList();
    }

    public string? SwitchDesktop(Guid desktop)
    {
        OperationResult r = _host.VirtualDesktops.SwitchToDesktop(desktop);
        return r.IsSuccess ? null : r.Error ?? "";
    }

    public string? CreateDesktop() => _acciones.Crear();

    public EliminacionEscritorio? DescribeRemoval(Guid desktop, int windows) => _acciones.Describir(desktop, windows);

    public string? RemoveDesktop(Guid desktop) => _acciones.Eliminar(desktop);

    public string? RenameDesktop(Guid desktop, string name)
    {
        OperationResult r = _host.VirtualDesktops.SetDesktopName(desktop, name);

        if (!r.IsSuccess)
        {
            return r.Error ?? "";
        }

        // Como «Guardar» en los ajustes: que se recuerde para restaurar la distribución.
        _host.DesktopLayout.Remember();
        return null;
    }

    public string? MoveWindowToDesktop(nint window, Guid desktop)
    {
        OperationResult r = _host.VirtualDesktops.MoveWindowToDesktop(window, desktop);
        return r.IsSuccess ? null : r.Error ?? "";
    }
}
