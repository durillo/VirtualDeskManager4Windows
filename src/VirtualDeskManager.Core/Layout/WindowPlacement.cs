using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Layout;

/// <summary>Dónde estaba una ventana y cómo.</summary>
/// <remarks>
/// Se guarda el proceso, la clase y el título, y no el HWND: el identificador de ventana lo
/// recicla Windows en cuanto la ventana se destruye, así que al volver de una suspensión el mismo
/// número puede ser ya otra ventana distinta (sección 9 de la especificación).
/// </remarks>
public sealed record WindowPlacement(
    string Process,
    string ClassName,
    string Title,
    WindowBounds Bounds,
    WindowVisualState State)
{
    /// <summary>Resumen legible de una línea.</summary>
    public override string ToString() => $"{Process} \"{Title}\" {Bounds} {State}";
}

/// <summary>La distribución completa de las ventanas con una configuración de pantallas dada.</summary>
public sealed record WindowLayoutSnapshot(
    DisplayFingerprint Fingerprint,
    DateTimeOffset Taken,
    IReadOnlyList<WindowPlacement> Placements)
{
    /// <summary>Resumen legible de una línea: huella, número de ventanas y fecha.</summary>
    public override string ToString() => $"{Fingerprint} ({Placements.Count} ventanas, {Taken:g})";
}

/// <summary>Una ventana que hay que devolver a su sitio.</summary>
public sealed record PlacementRestore(nint Handle, WindowPlacement Placement);
