using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>Una ventana dentro de un escritorio, con su sitio en la pantalla de 0 a 1.</summary>
/// <param name="Handle">La ventana.</param>
/// <param name="Programa">El programa (EXCEL, msedge…).</param>
/// <param name="Titulo">Su título.</param>
/// <param name="X">Dónde empieza, de 0 a 1 sobre todas las pantallas juntas.</param>
/// <param name="Y">Dónde empieza, de 0 a 1.</param>
/// <param name="Ancho">Cuánto ocupa, de 0 a 1.</param>
/// <param name="Alto">Cuánto ocupa, de 0 a 1.</param>
/// <param name="Minimizada">Si está minimizada: cuenta, pero no se dibuja.</param>
public sealed record VentanaEnEscritorio(nint Handle, string Programa, string Titulo, double X, double Y, double Ancho, double Alto, bool Minimizada);

/// <summary>Un escritorio virtual y sus ventanas, como lo cuenta el gestor.</summary>
/// <param name="Id">El escritorio.</param>
/// <param name="Numero">Su número, desde 1.</param>
/// <param name="Nombre">Su nombre; vacío si no tiene.</param>
/// <param name="Actual">Si es en el que se está.</param>
/// <param name="Ventanas">Sus ventanas, la de delante primero.</param>
public sealed record EscritorioDatos(Guid Id, int Numero, string Nombre, bool Actual, IReadOnlyList<VentanaEnEscritorio> Ventanas);

/// <summary>
/// «Mis escritorios»: una tarjeta por escritorio con sus ventanas en miniatura. Un clic cambia a
/// él; se pueden renombrar, crear y llevar una ventana de uno a otro arrastrándola (opción B,
/// elegida el 2026-10-10, maqueta en <c>docs/diseno/escritorios</c>).
/// </summary>
public sealed class EscritoriosVista : ObservableObject
{
    /// <summary>Los colores de las tarjetas, por posición (los escritorios no tienen uno guardado).</summary>
    public static readonly string[] Colores = { "#007AFF", "#FF9500", "#AF52DE", "#34C759", "#FF2D55", "#5AC8FA", "#FFCC00", "#8E8E93" };

    private readonly IDatosVentana _datos;
    private readonly DispatcherTimer _reloj = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _aviso = "";
    private string _huella = "";

    /// <summary>La sección sobre esos datos.</summary>
    public EscritoriosVista(IDatosVentana datos)
    {
        _datos = datos;
        NuevoCommand = new RelayCommand(Nuevo);
        _reloj.Tick += (_, _) => Refrescar();
    }

    /// <summary>
    /// Una tarjeta por escritorio, en orden, y al final la tarjeta «+» para crear otro
    /// (<see cref="NuevaTarjeta"/>).
    /// </summary>
    public ObservableCollection<object> Tarjetas { get; } = new();

    /// <summary>
    /// Pregunta algo al usuario y dice si ha contestado que sí. La pone la ventana; sin ella,
    /// lo que necesita confirmación no se hace.
    /// </summary>
    public Func<string, bool>? Confirmar { get; set; }

    /// <summary>Crear un escritorio al final.</summary>
    public RelayCommand NuevoCommand { get; }

    /// <summary>Cómo fue lo último (mover una ventana, renombrar…).</summary>
    public string Aviso
    {
        get => _aviso;
        set => Set(ref _aviso, value);
    }

    /// <summary>
    /// Se ve o deja de verse la sección. Mientras se ve se vuelve a leer cada 2 segundos: se puede
    /// cambiar de escritorio o mover ventanas desde fuera.
    /// </summary>
    public void Visible(bool seVe)
    {
        if (seVe)
        {
            Refrescar();
            _reloj.Start();
        }
        else
        {
            _reloj.Stop();
        }
    }

    /// <summary>Vuelve a leer los escritorios; sólo rehace las tarjetas si algo ha cambiado.</summary>
    public void Refrescar()
    {
        IReadOnlyList<EscritorioDatos> escritorios = _datos.Desktops();

        // Lo que se ve, en una línea: si no cambia, no se rehace nada (no parpadea ni pierde el menú abierto).
        string huella = string.Join("|", escritorios.Select(e =>
            $"{e.Id}:{e.Nombre}:{e.Actual}:" + string.Join(",", e.Ventanas.Select(v => $"{v.Handle}@{v.X:0.00},{v.Y:0.00},{v.Ancho:0.00},{v.Alto:0.00},{v.Minimizada}"))));

        if (huella == _huella)
        {
            return;
        }

        _huella = huella;
        Tarjetas.Clear();

        foreach (EscritorioDatos e in escritorios)
        {
            Tarjetas.Add(new EscritorioVista(e, Colores[(e.Numero - 1) % Colores.Length], this));
        }

        Tarjetas.Add(new NuevaTarjeta(NuevoCommand));
    }

    /// <summary>
    /// Elimina un escritorio. Sus ventanas pasan al de al lado; sólo se pregunta si tiene ventanas
    /// o alguna automatización lo usa.
    /// </summary>
    public void Eliminar(EscritorioVista escritorio)
    {
        EliminacionEscritorio? info = _datos.DescribeRemoval(escritorio.Datos.Id, escritorio.Datos.Ventanas.Count);

        if (info is null)
        {
            Aviso = Loc.Get("Desktops.RemoveLast");
            return;
        }

        if (info.Pregunta() is { } pregunta && Confirmar?.Invoke(pregunta) != true)
        {
            return;
        }

        Aviso = _datos.RemoveDesktop(escritorio.Datos.Id) ?? info.Hecho();
        _huella = "";
        Refrescar();
    }

    /// <summary>Si se puede eliminar alguno: hace falta más de uno.</summary>
    public bool HayVarios => Tarjetas.OfType<EscritorioVista>().Skip(1).Any();

    internal void IrA(EscritorioVista escritorio)
    {
        Aviso = _datos.SwitchDesktop(escritorio.Datos.Id) ?? "";
        _huella = "";
        Refrescar();
    }

    /// <summary>Le pone nombre a un escritorio (vacío: el de Windows).</summary>
    public void Renombrar(EscritorioVista escritorio, string nombre)
    {
        Aviso = _datos.RenameDesktop(escritorio.Datos.Id, nombre.Trim()) ?? "";
        _huella = "";
        Refrescar();
    }

    /// <summary>Lleva una ventana a otro escritorio (al soltarla en su tarjeta).</summary>
    public void Mover(nint ventana, EscritorioVista destino)
    {
        if (destino.Datos.Ventanas.Any(v => v.Handle == ventana))
        {
            return;
        }

        string? error = _datos.MoveWindowToDesktop(ventana, destino.Datos.Id);
        Aviso = error ?? Loc.Format("Vdesk.WindowMoved", destino.Nombre);
        _huella = "";
        Refrescar();
    }

    private void Nuevo()
    {
        Aviso = _datos.CreateDesktop() ?? "";
        _huella = "";
        Refrescar();
    }
}

/// <summary>La tarjeta «+» del final de «Mis escritorios»: crea un escritorio.</summary>
/// <param name="Crear">Lo mismo que el botón «Nuevo escritorio» de arriba.</param>
public sealed record NuevaTarjeta(RelayCommand Crear);

/// <summary>Una tarjeta de «Mis escritorios».</summary>
public sealed class EscritorioVista
{
    /// <summary>El ancho y el alto de la miniatura, en puntos.</summary>
    public const double AnchoMiniatura = 226, AltoMiniatura = 124;

    private readonly EscritoriosVista _seccion;

    /// <summary>La tarjeta de un escritorio.</summary>
    public EscritorioVista(EscritorioDatos datos, string color, EscritoriosVista seccion)
    {
        Datos = datos;
        Color = color;
        _seccion = seccion;
        IrCommand = new RelayCommand(() => _seccion.IrA(this));

        // La de delante, la última: así queda encima. Como mucho 8, que no se vea un borrón.
        Miniaturas = datos.Ventanas
            .Where(v => !v.Minimizada && v.Ancho > 0.02 && v.Alto > 0.02)
            .Take(8)
            .Reverse()
            .Select(v => new VentanaMiniatura(v, AnchoMiniatura, AltoMiniatura))
            .ToList();
    }

    /// <summary>Lo que cuenta el gestor de este escritorio.</summary>
    public EscritorioDatos Datos { get; }

    /// <summary>Su nombre, o «Escritorio N».</summary>
    public string Nombre => Datos.Nombre.Length > 0 ? Datos.Nombre : Loc.Format("Vdesk.DesktopN", Datos.Numero);

    /// <summary>Su color, por posición.</summary>
    public string Color { get; }

    /// <summary>Si es el actual.</summary>
    public bool Actual => Datos.Actual;

    /// <summary>«3 ventanas».</summary>
    public string Cuantas => Datos.Ventanas.Count == 1 ? Loc.Get("Vdesk.OneWindow") : Loc.Format("Vdesk.WindowsCount", Datos.Ventanas.Count);

    /// <summary>Las ventanas que se dibujan en la miniatura.</summary>
    public IReadOnlyList<VentanaMiniatura> Miniaturas { get; }

    /// <summary>Cambiar a este escritorio.</summary>
    public RelayCommand IrCommand { get; }

    /// <summary>La sección, para renombrar y mover desde la vista.</summary>
    public EscritoriosVista Seccion => _seccion;
}

/// <summary>Una ventana dibujada en la miniatura de su escritorio.</summary>
public sealed class VentanaMiniatura
{
    /// <summary>La ventana, a la escala de la miniatura.</summary>
    public VentanaMiniatura(VentanaEnEscritorio ventana, double ancho, double alto)
    {
        Ventana = ventana;
        Izquierda = Math.Clamp(ventana.X, 0, 1) * ancho;
        Arriba = Math.Clamp(ventana.Y, 0, 1) * alto;
        Ancho = Math.Max(24, Math.Min(ventana.Ancho, 1 - Math.Clamp(ventana.X, 0, 1)) * ancho);
        Alto = Math.Max(18, Math.Min(ventana.Alto, 1 - Math.Clamp(ventana.Y, 0, 1)) * alto);
    }

    /// <summary>La ventana de verdad.</summary>
    public VentanaEnEscritorio Ventana { get; }

    /// <summary>Dónde va en la miniatura.</summary>
    public double Izquierda { get; }

    /// <summary>Dónde va en la miniatura.</summary>
    public double Arriba { get; }

    /// <summary>Su tamaño en la miniatura.</summary>
    public double Ancho { get; }

    /// <summary>Su tamaño en la miniatura.</summary>
    public double Alto { get; }

    /// <summary>El programa, para la etiqueta.</summary>
    public string Programa => Ventana.Programa;

    /// <summary>El programa y el título, al pasar el ratón.</summary>
    public string Detalle => $"{Ventana.Programa} — {Ventana.Titulo}";
}
