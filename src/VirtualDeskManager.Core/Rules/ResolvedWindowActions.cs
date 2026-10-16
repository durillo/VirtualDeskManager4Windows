using System.Globalization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>
/// Result of merging every matching rule, property by property, honouring priority
/// (spec section 22). <see cref="Sources"/> records which rule won each property.
/// </summary>
public sealed class ResolvedWindowActions
{
    /// <summary>Escritorio ganador; ver <see cref="WindowActions.Desktop"/>.</summary>
    public string? Desktop { get; set; }

    /// <summary>«Siempre encima» ganador; ver <see cref="WindowActions.AlwaysOnTop"/>.</summary>
    public bool? AlwaysOnTop { get; set; }

    /// <summary>Anclaje de la ventana ganador; ver <see cref="WindowActions.PinWindow"/>.</summary>
    public bool? PinWindow { get; set; }

    /// <summary>Anclaje de la aplicación ganador; ver <see cref="WindowActions.PinApplication"/>.</summary>
    public bool? PinApplication { get; set; }

    /// <summary>Monitor ganador, contando desde uno; ver <see cref="WindowActions.Monitor"/>.</summary>
    public int? Monitor { get; set; }

    /// <summary>Maximizado ganador; ver <see cref="WindowActions.Maximized"/>.</summary>
    public bool? Maximized { get; set; }

    /// <summary>Posición y tamaño ganadores; ver <see cref="WindowActions.Bounds"/>.</summary>
    public WindowBounds? Bounds { get; set; }

    /// <summary>Go to the window the application already has instead of keeping the new one.</summary>
    public bool? ReuseExistingWindow { get; set; }

    /// <summary>Dejar abrir una ventana nueva aunque la aplicacion ya tenga otra en otro sitio.</summary>
    public bool? NewInstance { get; set; }

    /// <summary>Property name to the name of the rule that decided it.</summary>
    public Dictionary<string, string> Sources { get; } = new(StringComparer.Ordinal);

    /// <summary>Si alguna regla ha pedido algo; si no, no hay nada que aplicar.</summary>
    public bool HasAny =>
        Desktop is not null
        || AlwaysOnTop is not null
        || PinWindow is not null
        || PinApplication is not null
        || Monitor is not null
        || Maximized is not null
        || Bounds is not null
        || ReuseExistingWindow is not null
        || NewInstance is not null;

    /// <summary>
    /// Stable fingerprint of the resolved actions. Used by the loop guard so that re-applying
    /// the exact same thing to the same window is skipped (spec section 23).
    /// </summary>
    public string ComputeSignature()
    {
        List<string> parts = new();

        if (AlwaysOnTop is not null)
        {
            parts.Add("top:" + AlwaysOnTop.Value);
        }

        if (PinWindow is not null)
        {
            parts.Add("pinw:" + PinWindow.Value);
        }

        if (PinApplication is not null)
        {
            parts.Add("pina:" + PinApplication.Value);
        }

        if (Desktop is not null)
        {
            parts.Add("desk:" + Desktop);
        }

        if (Monitor is not null)
        {
            parts.Add("mon:" + Monitor.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Maximized is not null)
        {
            parts.Add("max:" + Maximized.Value);
        }

        if (Bounds is not null)
        {
            parts.Add("rect:" + Bounds.Value);
        }

        if (NewInstance is not null)
        {
            parts.Add("new:" + NewInstance.Value);
        }

        if (ReuseExistingWindow is not null)
        {
            parts.Add("reuse:" + ReuseExistingWindow.Value);
        }

        return string.Join("|", parts);
    }
}
