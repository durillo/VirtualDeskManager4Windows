using System;
using System.Collections.Generic;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>
/// Lo que necesita la ventana principal. La de verdad sale de <see cref="AppHost"/>; el banco de
/// capturas pone datos de ejemplo.
/// </summary>
public interface IDatosVentana
{
    /// <summary>Guarda los ajustes después de tocar algo.</summary>
    void Save();

    /// <summary>Abre los ajustes, en una sección si se dice (por ejemplo «hotkeys»).</summary>
    void OpenSettings(string? section);

    /// <summary>La ventana en oscuro; se guarda al cambiarla.</summary>
    bool Dark { get; set; }

    /// <summary>Los escritorios y sus ventanas, en orden. En el hilo de la interfaz.</summary>
    IReadOnlyList<EscritorioDatos> Desktops();

    /// <summary>Cambia a ese escritorio. Error en palabras, o nulo.</summary>
    string? SwitchDesktop(Guid desktop);

    /// <summary>Crea un escritorio al final. Error en palabras, o nulo.</summary>
    string? CreateDesktop();

    /// <summary>Lo que hay que contar antes de eliminar un escritorio, o nulo si no se puede.</summary>
    EliminacionEscritorio? DescribeRemoval(Guid desktop, int windows);

    /// <summary>Elimina un escritorio (sus ventanas pasan al de al lado). Error en palabras, o nulo.</summary>
    string? RemoveDesktop(Guid desktop);

    /// <summary>Le pone nombre a un escritorio y lo recuerda. Error en palabras, o nulo.</summary>
    string? RenameDesktop(Guid desktop, string name);

    /// <summary>Lleva una ventana a otro escritorio. Error en palabras, o nulo.</summary>
    string? MoveWindowToDesktop(nint window, Guid desktop);

    /// <summary>
    /// El modelo de los ajustes (para las opciones de escritorios y las páginas de Automatizaciones
    /// y Distribuir ventanas); nulo en las capturas.
    /// </summary>
    object? Settings { get; }
}

/// <summary>Las secciones de la barra lateral.</summary>
public enum SeccionVdesk
{
    /// <summary>Los escritorios virtuales, en tarjetas.</summary>
    Escritorios,
    /// <summary>Lo que se hace solo al abrirse una ventana (las reglas).</summary>
    Automatizaciones,
    /// <summary>Las zonas para encajar ventanas.</summary>
    Distribuir
}

/// <summary>
/// La ventana principal de Virtual Desk Manager (estilo macOS, pedida el 2026-10-10): barra
/// lateral con los escritorios virtuales y un engranaje que lleva a los ajustes.
/// </summary>
public sealed class VentanaVdeskViewModel : ObservableObject
{
    private readonly IDatosVentana _datos;
    private SeccionVdesk _seccion = SeccionVdesk.Escritorios;

    /// <summary>La ventana sobre esos datos.</summary>
    public VentanaVdeskViewModel(IDatosVentana datos)
    {
        _datos = datos;
        AjustesCommand = new RelayCommand(() => _datos.OpenSettings(null));
        Escritorios = new EscritoriosVista(datos);
    }

    /// <summary>La sección Mis escritorios.</summary>
    public EscritoriosVista Escritorios { get; }

    /// <summary>El modelo de los ajustes, para las opciones de escritorios; nulo en las capturas.</summary>
    public object? Ajustes => _datos.Settings;

    /// <summary>La ventana en oscuro (se guarda).</summary>
    public bool Oscuro
    {
        get => _datos.Dark;
        set
        {
            if (_datos.Dark != value)
            {
                _datos.Dark = value;
                _datos.Save();
                Raise(nameof(Oscuro));
            }
        }
    }

    /// <summary>Empieza a leer los escritorios si es su sección (al abrir la ventana).</summary>
    public void Abierta() => Escritorios.Visible(Seccion == SeccionVdesk.Escritorios);

    /// <summary>Deja de leerlos (al cerrar la ventana).</summary>
    public void Cerrada() => Escritorios.Visible(false);

    /// <summary>El engranaje: abre los ajustes.</summary>
    public RelayCommand AjustesCommand { get; }

    /// <summary>La sección abierta.</summary>
    public SeccionVdesk Seccion
    {
        get => _seccion;
        set
        {
            if (Set(ref _seccion, value))
            {
                Raise(nameof(Titulo));
                Raise(nameof(Subtitulo));
                Raise(nameof(EnEscritorios));
                Raise(nameof(EnAutomatizaciones));
                Raise(nameof(EnDistribuir));
                Escritorios.Visible(value == SeccionVdesk.Escritorios);
            }
        }
    }

    /// <summary>Está abierta Mis escritorios.</summary>
    public bool EnEscritorios => Seccion == SeccionVdesk.Escritorios;

    /// <summary>Está abierta Automatizaciones.</summary>
    public bool EnAutomatizaciones => Seccion == SeccionVdesk.Automatizaciones;

    /// <summary>Está abierta Distribuir ventanas.</summary>
    public bool EnDistribuir => Seccion == SeccionVdesk.Distribuir;

    /// <summary>El título grande de la sección.</summary>
    public string Titulo => Loc.Get(Seccion switch
    {
        SeccionVdesk.Automatizaciones => "Nav.Automations",
        SeccionVdesk.Distribuir => "Nav.Zones",
        _ => "Nav.Desktops"
    });

    /// <summary>La línea de explicación bajo el título.</summary>
    public string Subtitulo => Loc.Get(Seccion switch
    {
        SeccionVdesk.Automatizaciones => "Page.Automations.Intro",
        SeccionVdesk.Distribuir => "Page.Zones.Intro",
        _ => "Vdesk.DesktopsHint"
    });
}
