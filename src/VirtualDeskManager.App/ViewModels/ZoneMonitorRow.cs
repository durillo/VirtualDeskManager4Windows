using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Zones;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>Un reparto en el desplegable, con su nombre traducido.</summary>
public sealed class ZoneTemplateChoice
{
    /// <summary>La opción de un reparto.</summary>
    public ZoneTemplateChoice(ZoneTemplate template) => Template = template;

    /// <summary>El reparto que representa.</summary>
    public ZoneTemplate Template { get; }

    /// <summary>Su nombre en el idioma de la interfaz.</summary>
    public string Display => Loc.Get("Zone." + Template);

    /// <summary>Lo mismo que <see cref="Display"/>.</summary>
    public override string ToString() => Display;
}

/// <summary>Un rectángulo de la vista previa, ya en unidades de la pantalla de ajustes.</summary>
public sealed record ZonePreviewRect(double Left, double Top, double Width, double Height, int Number);

/// <summary>
/// El reparto de un monitor tal y como se edita en la pestaña de zonas.
/// </summary>
/// <remarks>
/// Lleva su propia vista previa porque es lo único que hace entendible la diferencia entre
/// "cuadrícula de seis" y "columnas de seis" sin tener que probarlas moviendo ventanas de verdad.
/// </remarks>
public sealed class ZoneMonitorRow : ObservableObject
{
    /// <summary>Lo más grande que se deja crecer la vista previa, en unidades de WPF.</summary>
    private const double MaximumPreviewWidth = 300;

    private const double MaximumPreviewHeight = 140;

    private readonly MonitorInfo _monitor;
    private readonly MonitorZoneSettings _settings;
    private readonly Action _changed;

    private ZoneTemplateChoice _selectedTemplate;

    /// <summary>Prepara la fila de un monitor con el reparto que tiene guardado.</summary>
    /// <param name="monitor">El monitor.</param>
    /// <param name="settings">Su reparto en los ajustes; la fila lo cambia directamente.</param>
    /// <param name="changed">Se llama tras cada cambio, para guardar los ajustes.</param>
    public ZoneMonitorRow(MonitorInfo monitor, MonitorZoneSettings settings, Action changed)
    {
        _monitor = monitor;
        _settings = settings;
        _changed = changed;

        foreach (ZoneTemplate template in Enum.GetValues<ZoneTemplate>())
        {
            Templates.Add(new ZoneTemplateChoice(template));
        }

        _selectedTemplate = Templates.First(choice => choice.Template == settings.Template);

        Rebuild();
    }

    /// <summary>Los repartos que ofrece el desplegable «Reparto».</summary>
    public ObservableCollection<ZoneTemplateChoice> Templates { get; } = new();

    /// <summary>Los rectángulos de la vista previa «Cómo queda», numerados como las zonas.</summary>
    public ObservableCollection<ZonePreviewRect> Preview { get; } = new();

    /// <summary>«Monitor 2 · 1920×1080», y le añade cuál es el principal.</summary>
    public string Header
    {
        get
        {
            string text = Loc.Format("Zones.Monitor", _monitor.Number)
                          + "   ·   " + _monitor.Bounds.Width + "×" + _monitor.Bounds.Height;

            // Una estrella para el principal y no una palabra: no hace falta traducirla y se ve
            // de un vistazo cuál es cuál sin alargar la cabecera.
            return _monitor.IsPrimary ? text + "   ★" : text;
        }
    }

    /// <summary>El desplegable «Reparto». Al cambiarlo se guarda y se redibuja la vista previa.</summary>
    public ZoneTemplateChoice SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (value is null || !Set(ref _selectedTemplate, value))
            {
                return;
            }

            _settings.Template = value.Template;
            Apply();
        }
    }

    /// <summary>
    /// El control «Zonas»: en cuántas se reparte el monitor, de 1 a 9. Al cambiarlo se guarda y se
    /// redibuja la vista previa.
    /// </summary>
    public int Count
    {
        get => _settings.Count;
        set
        {
            if (_settings.Count == value)
            {
                return;
            }

            _settings.Count = value;
            Apply();
            Raise();
        }
    }

    /// <summary>
    /// El control «Separación»: los píxeles entre zonas, de 0 a 40. Al cambiarlo se guarda y se
    /// redibuja la vista previa.
    /// </summary>
    public int Gap
    {
        get => _settings.Gap;
        set
        {
            if (_settings.Gap == value)
            {
                return;
            }

            _settings.Gap = value;
            Apply();
            Raise();
        }
    }

    /// <summary>El número y el hueco no pintan nada si el monitor no está repartido.</summary>
    public bool HasZones => _settings.Template != ZoneTemplate.None;

    /// <summary>El monitor no está repartido: se enseña «Este monitor no está repartido.».</summary>
    public bool HasNoZones => !HasZones;

    private void Apply()
    {
        _settings.Normalise();
        Rebuild();
        _changed();
    }

    /// <summary>El recuadro de la vista previa, con la proporción exacta de este monitor.</summary>
    /// <remarks>
    /// Calculado y no fijo. Con un recuadro de tamaño fijo, un monitor 16:9 dejaba una franja
    /// vacía a un lado y el ultrapanorámico otra debajo, y el resultado parecía descuadrado sin
    /// que se entendiera por qué.
    /// </remarks>
    public double PreviewWidth { get; private set; } = MaximumPreviewWidth;

    /// <summary>El alto del recuadro de la vista previa; ver <see cref="PreviewWidth"/>.</summary>
    public double PreviewHeight { get; private set; } = MaximumPreviewHeight;

    /// <summary>Las esquinas sólo se redondean cuando hay separación entre las zonas.</summary>
    /// <remarks>
    /// Sin separación, redondear las esquinas dibuja un hueco donde dos zonas se tocan, y lo que
    /// se ve es justo lo contrario de lo que el reparto dice.
    /// </remarks>
    public double ZoneCorner => _settings.Gap > 0 ? 3 : 0;

    /// <summary>
    /// Vuelve a calcular los rectángulos de la vista previa.
    /// </summary>
    /// <remarks>
    /// Se calculan con las zonas de verdad, en píxeles del monitor, y sólo después se escalan: así
    /// la vista previa no puede mentir, porque enseña exactamente lo que se le va a aplicar a las
    /// ventanas y no un dibujo parecido hecho aparte.
    /// </remarks>
    private void Rebuild()
    {
        Preview.Clear();

        IReadOnlyList<WindowBounds> zones = _settings.Compute(_monitor.WorkingArea);
        WindowBounds area = _monitor.WorkingArea;

        if (zones.Count == 0 || area.IsEmpty)
        {
            Announce();
            return;
        }

        // Se mantiene la proporción del monitor: un ultrapanorámico tiene que verse alargado en la
        // vista previa, o el reparto que uno elige mirándola no es el que luego encuentra.
        double scale = Math.Min(MaximumPreviewWidth / area.Width, MaximumPreviewHeight / area.Height);

        PreviewWidth = Math.Round(area.Width * scale);
        PreviewHeight = Math.Round(area.Height * scale);

        foreach (WindowBounds zone in zones)
        {
            // Se redondean los cuatro bordes y el tamaño sale de restarlos, en vez de redondear
            // posición y tamaño por separado. Haciéndolo por separado, el borde derecho de una
            // zona y el izquierdo de la siguiente caen en números distintos y aparece una rendija
            // entre dos zonas que en realidad se tocan.
            double left = Math.Round((zone.X - area.X) * scale);
            double top = Math.Round((zone.Y - area.Y) * scale);
            double right = Math.Round((zone.Right - area.X) * scale);
            double bottom = Math.Round((zone.Bottom - area.Y) * scale);

            Preview.Add(new ZonePreviewRect(
                left,
                top,
                Math.Max(1, right - left),
                Math.Max(1, bottom - top),
                Preview.Count + 1));
        }

        Announce();
    }

    private void Announce()
    {
        Raise(nameof(HasZones));
        Raise(nameof(HasNoZones));
        Raise(nameof(PreviewWidth));
        Raise(nameof(PreviewHeight));
        Raise(nameof(ZoneCorner));
    }
}
