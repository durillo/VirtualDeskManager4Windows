using System;
using System.Collections.Generic;
using System.Linq;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App;

/// <summary>
/// Lo que hay que contarle al usuario antes de eliminar un escritorio.
/// </summary>
/// <param name="Nombre">Cómo se llama (o «Escritorio N»).</param>
/// <param name="Destino">El escritorio al que pasan sus ventanas.</param>
/// <param name="Ventanas">Cuántas ventanas tiene.</param>
/// <param name="Automatizaciones">Cuántas automatizaciones llevan aplicaciones a él.</param>
public sealed record EliminacionEscritorio(string Nombre, string Destino, int Ventanas, int Automatizaciones)
{
    /// <summary>
    /// La pregunta de confirmación, o null si no hace falta preguntar: un escritorio vacío y que
    /// ninguna automatización usa se elimina sin más (pedido el 2026-10-13).
    /// </summary>
    public string? Pregunta()
    {
        if (Ventanas == 0 && Automatizaciones == 0)
        {
            return null;
        }

        List<string> partes = new() { Loc.Format("Desktops.RemoveQuestion", Nombre) };

        if (Ventanas > 0)
        {
            partes.Add(Ventanas == 1
                ? Loc.Format("Desktops.RemoveOneWindow", Destino)
                : Loc.Format("Desktops.RemoveWindows", Ventanas, Destino));
        }

        if (Automatizaciones > 0)
        {
            partes.Add(Automatizaciones == 1
                ? Loc.Get("Desktops.RemoveOneRule")
                : Loc.Format("Desktops.RemoveRules", Automatizaciones));
        }

        return string.Join("\n\n", partes);
    }

    /// <summary>Lo que se dice cuando ya está hecho.</summary>
    public string Hecho() => Ventanas > 0
        ? Loc.Format("Desktops.RemovedWithWindows", Nombre, Destino)
        : Loc.Format("Desktops.Removed", Nombre);
}

/// <summary>
/// Crear y eliminar escritorios, igual desde «Mis escritorios» que desde la vista de todos los
/// escritorios.
/// </summary>
/// <remarks>
/// En un solo sitio para que las dos pantallas hagan lo mismo: quedarse donde se estaba, recordar
/// el reparto (así lo eliminado no vuelve a aparecer al arrancar) y contar lo mismo.
/// </remarks>
public sealed class AccionesEscritorios
{
    private readonly AppHost _host;

    /// <summary>Las acciones sobre los escritorios de esa aplicación.</summary>
    public AccionesEscritorios(AppHost host) => _host = host;

    private IVirtualDesktopService Escritorios => _host.VirtualDesktops;

    /// <summary>Si el motor en uso sabe eliminar escritorios.</summary>
    public bool PuedeEliminar => Escritorios.Capabilities.CanRemoveDesktop;

    /// <summary>Si el motor en uso sabe crear escritorios.</summary>
    public bool PuedeCrear => Escritorios.Capabilities.CanCreateDesktop;

    /// <summary>Crea uno al final y se queda en el escritorio en el que se estaba. Error en palabras, o null.</summary>
    public string? Crear()
    {
        Guid? antes = Escritorios.GetCurrentDesktop()?.Id;
        VirtualDesktopInfo? nuevo = Escritorios.CreateDesktop();

        // Windows se va al que acaba de crear: se vuelve a donde se estaba.
        if (antes is { } donde)
        {
            Escritorios.SwitchToDesktop(donde);
        }

        if (nuevo is null)
        {
            return Loc.Get("Common.Failed");
        }

        _host.DesktopLayout.Remember();
        return null;
    }

    /// <summary>Lo que hay que saber antes de eliminarlo, o null si no se puede (es el único o ya no existe).</summary>
    /// <param name="escritorio">El escritorio.</param>
    /// <param name="ventanas">Cuántas ventanas tiene, que ya sabe quien pregunta.</param>
    public EliminacionEscritorio? Describir(Guid escritorio, int ventanas)
    {
        IReadOnlyList<VirtualDesktopInfo> todos = Escritorios.GetDesktops();
        VirtualDesktopInfo? este = todos.FirstOrDefault(d => d.Id == escritorio);
        VirtualDesktopInfo? destino = DesktopRemoval.Destination(todos, escritorio);

        if (este is null || destino is null)
        {
            return null;
        }

        int automatizaciones = DesktopRemoval.RulesUsing(_host.Configuration.Rules, todos, escritorio).Count;

        return new EliminacionEscritorio(Nombre(este), Nombre(destino), ventanas, automatizaciones);
    }

    /// <summary>Lo elimina (sus ventanas pasan al de al lado) y lo olvida. Error en palabras, o null.</summary>
    public string? Eliminar(Guid escritorio)
    {
        if (!PuedeEliminar)
        {
            return Loc.Get("Desktops.RemoveUnsupported");
        }

        if (!DesktopRemoval.CanRemove(Escritorios.GetDesktops(), escritorio))
        {
            return Loc.Get("Desktops.RemoveLast");
        }

        OperationResult r = Escritorios.RemoveDesktop(escritorio);

        if (!r.IsSuccess)
        {
            return string.IsNullOrWhiteSpace(r.Error) ? Loc.Get("Common.Failed") : r.Error;
        }

        // Si no, al arrancar la próxima vez se repondría, porque faltaría respecto a lo guardado.
        _host.DesktopLayout.Remember();
        return null;
    }

    /// <summary>Le pone nombre (vacío: el de Windows) y lo recuerda. Error en palabras, o null.</summary>
    public string? Renombrar(Guid escritorio, string nombre)
    {
        OperationResult r = Escritorios.SetDesktopName(escritorio, nombre.Trim());

        if (!r.IsSuccess)
        {
            return string.IsNullOrWhiteSpace(r.Error) ? Loc.Get("Common.Failed") : r.Error;
        }

        _host.DesktopLayout.Remember();
        return null;
    }

    private static string Nombre(VirtualDesktopInfo d) =>
        d.HasName ? d.Name : Loc.Format("Overlay.DesktopNumber", d.Number);
}
