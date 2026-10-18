using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Zones;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>El reparto en zonas de un monitor concreto.</summary>
public sealed class MonitorZoneSettings
{
    /// <summary>Número del monitor, el mismo que usan las reglas.</summary>
    public int Monitor { get; set; } = 1;

    /// <summary>Cómo se reparte el monitor. De serie, <see cref="ZoneTemplate.None"/>: el monitor entero, sin zonas.</summary>
    public ZoneTemplate Template { get; set; } = ZoneTemplate.None;

    /// <summary>
    /// Cuántas zonas, de <see cref="ZoneLayout.MinimumZones"/> a <see cref="ZoneLayout.MaximumZones"/>.
    /// 3 de serie. Con <see cref="ZoneTemplate.None"/> no cuenta: siempre hay una.
    /// </summary>
    public int Count { get; set; } = 3;

    /// <summary>Hueco entre zonas, en píxeles.</summary>
    public int Gap { get; set; }

    /// <summary>Lleva monitor, número de zonas y hueco a valores válidos.</summary>
    public void Normalise()
    {
        Monitor = Math.Max(1, Monitor);
        Count = Math.Clamp(Count, ZoneLayout.MinimumZones, ZoneLayout.MaximumZones);
        Gap = Math.Clamp(Gap, 0, ZoneLayout.MaximumGap);
    }

    /// <summary>Los rectángulos de las zonas dentro del área útil del monitor, en píxeles de pantalla.</summary>
    public IReadOnlyList<WindowBounds> Compute(WindowBounds workingArea) =>
        ZoneLayout.Compute(workingArea, Template, Count, Gap);
}

/// <summary>
/// Zonas por monitor, al estilo de FancyZones.
/// </summary>
/// <remarks>
/// Un reparto por monitor y no uno para todos: un ultrapanorámico pide tres o cuatro columnas y
/// un portátil de al lado no admite más de dos sin que nada quepa. Que la misma configuración
/// valga para los dos es justamente lo que hace inútiles las zonas.
/// </remarks>
public sealed class ZoneSettings
{
    /// <summary>Si las zonas funcionan. Apagado de serie; apagadas, los atajos de zona responden que no está disponible.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Enseñar las zonas al arrastrar con Mayús y encajar la ventana donde se suelte.
    /// </summary>
    /// <remarks>
    /// Viene activado, porque es lo que le da sentido a las zonas: con atajos hay que acordarse de
    /// qué número es cada sitio, y arrastrando se ve. No hace nada mientras las zonas no estén
    /// encendidas, así que activarlo de serie no le cambia nada a quien no las use.
    /// </remarks>
    public bool SnapWhileDragging { get; set; } = true;

    /// <summary>
    /// Cuadrar las esquinas de las ventanas encajadas.
    /// </summary>
    /// <remarks>
    /// Windows 11 redondea las esquinas, y dos ventanas que se tocan al pixel dejan ver el
    /// escritorio por el redondeo, arriba y abajo de la junta. No es un hueco, pero se ve como un
    /// hueco. Al apagar esto o al cerrar la aplicacion, cada ventana recupera las suyas.
    /// </remarks>
    public bool SeamlessEdges { get; set; } = true;

    /// <summary>El reparto de cada monitor. Un monitor que no está aquí no tiene zonas; se crea con <see cref="For"/>.</summary>
    public List<MonitorZoneSettings> Monitors { get; set; } = new();

    /// <summary>Si ya se han ofrecido los atajos de zona a esta instalación.</summary>
    public bool HotkeysAdded { get; set; }

    /// <summary>El reparto de un monitor, creándolo sin zonas si aún no estaba.</summary>
    public MonitorZoneSettings For(int monitor)
    {
        MonitorZoneSettings? found = Monitors.FirstOrDefault(one => one.Monitor == monitor);

        if (found is not null)
        {
            return found;
        }

        MonitorZoneSettings created = new() { Monitor = monitor };
        Monitors.Add(created);
        return created;
    }

    /// <summary>La zona más alta configurada en cualquier monitor: cuántos atajos hacen falta.</summary>
    public int MaximumZoneCount =>
        Monitors.Count == 0
            ? 0
            : Monitors.Max(monitor => ZoneLayout.CountFor(monitor.Template, monitor.Count));

    /// <summary>Normaliza cada monitor, quita los repetidos (se queda el primero) y los ordena por número.</summary>
    public void Normalise()
    {
        foreach (MonitorZoneSettings monitor in Monitors)
        {
            monitor.Normalise();
        }

        // Un monitor repetido significaria dos repartos distintos para la misma pantalla, y cuál
        // gana dependeria del orden del fichero.
        Monitors = Monitors
            .GroupBy(monitor => monitor.Monitor)
            .Select(group => group.First())
            .OrderBy(monitor => monitor.Monitor)
            .ToList();
    }
}
