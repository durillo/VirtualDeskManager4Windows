namespace VirtualDeskManager.Core.Models;

/// <summary>
/// Un monitor del equipo, como lo ve el resto de la aplicación.
/// </summary>
/// <param name="Number">Posición en la lista de Windows, empezando en uno. Es lo que guarda la regla.</param>
/// <param name="Bounds">El monitor entero, en píxeles físicos.</param>
/// <param name="WorkingArea">Lo mismo sin la barra de tareas: donde se colocan las ventanas.</param>
/// <param name="IsPrimary">Si es el monitor principal de Windows, el que lleva la barra de tareas con la bandeja.</param>
/// <remarks>
/// El número sale de una sola parte para todos. Si el desplegable del editor contara los
/// monitores por su cuenta y el movimiento los contara por la suya, «Monitor 2» podría significar
/// pantallas distintas en cada sitio, y la regla mandaría la ventana a la que no es.
/// </remarks>
public sealed record MonitorInfo(int Number, WindowBounds Bounds, WindowBounds WorkingArea, bool IsPrimary)
{
    /// <summary>Número y tamaño para el diario, p. ej. <c>2: 2560x1440 (primary)</c>.</summary>
    public override string ToString() =>
        $"{Number}: {Bounds.Width}x{Bounds.Height}" + (IsPrimary ? " (primary)" : "");
}
