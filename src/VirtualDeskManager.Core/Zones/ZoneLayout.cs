using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Zones;

/// <summary>Cómo se reparte un monitor en zonas.</summary>
public enum ZoneTemplate
{
    /// <summary>El monitor entero: una sola zona. Es lo mismo que no tener zonas.</summary>
    None,

    /// <summary>Columnas de igual ancho, de izquierda a derecha.</summary>
    Columns,

    /// <summary>Filas de igual alto, de arriba abajo.</summary>
    Rows,

    /// <summary>Una cuadrícula lo más cuadrada posible para el número de zonas pedido.</summary>
    Grid,

    /// <summary>Una zona grande a la izquierda y el resto apiladas a la derecha.</summary>
    PriorityLeft,

    /// <summary>La misma, con la grande a la derecha.</summary>
    PriorityRight
}

/// <summary>
/// Reparte el área útil de un monitor en zonas rectangulares.
/// </summary>
/// <remarks>
/// Aritmética pura y sin Win32, como la rejilla de escritorios, para poder probarla (sección 40).
/// Trabaja sobre el <b>área útil</b> del monitor y no sobre el monitor entero, porque una zona que
/// se meta debajo de la barra de tareas deja la ventana con el borde inferior inalcanzable.
/// </remarks>
public static class ZoneLayout
{
    /// <summary>Más zonas que esto en una pantalla no son zonas, son rendijas.</summary>
    public const int MaximumZones = 9;

    /// <summary>Lo menos que se puede pedir: una zona, que es el área útil entera.</summary>
    public const int MinimumZones = 1;

    /// <summary>Hueco máximo entre zonas, en píxeles.</summary>
    public const int MaximumGap = 40;

    /// <summary>Por debajo de esto una zona no da para ninguna ventana usable.</summary>
    private const int MinimumZoneSize = 80;

    /// <summary>
    /// Las zonas de una plantilla sobre el área útil. Vacío si el área está vacía.
    /// </summary>
    /// <param name="workingArea">El área útil del monitor, sin la barra de tareas.</param>
    /// <param name="template">Cómo repartirla.</param>
    /// <param name="count">Cuántas zonas; se ajusta a entre <see cref="MinimumZones"/> y <see cref="MaximumZones"/>.</param>
    /// <param name="gap">Hueco entre zonas en píxeles, hasta <see cref="MaximumGap"/>.</param>
    public static IReadOnlyList<WindowBounds> Compute(
        WindowBounds workingArea,
        ZoneTemplate template,
        int count,
        int gap = 0)
    {
        if (workingArea.IsEmpty)
        {
            return Array.Empty<WindowBounds>();
        }

        count = Math.Clamp(count, MinimumZones, MaximumZones);
        gap = Math.Clamp(gap, 0, MaximumGap);

        List<WindowBounds> zones = template switch
        {
            ZoneTemplate.Columns => Split(workingArea, count, 1),
            ZoneTemplate.Rows => Split(workingArea, 1, count),
            ZoneTemplate.Grid => Grid(workingArea, count),
            ZoneTemplate.PriorityLeft => Priority(workingArea, count, bigOnLeft: true),
            ZoneTemplate.PriorityRight => Priority(workingArea, count, bigOnLeft: false),
            _ => new List<WindowBounds> { workingArea }
        };

        return gap == 0 ? zones : zones.Select(zone => Shrink(zone, gap)).ToList();
    }

    /// <summary>
    /// Cuántas zonas tiene de verdad una plantilla.
    /// </summary>
    /// <remarks>
    /// La cuadrícula redondea hacia arriba —cinco zonas caben en tres por dos y sobra una—, así
    /// que la interfaz no puede fiarse del número pedido para decir cuántos atajos hacen falta.
    /// </remarks>
    public static int CountFor(ZoneTemplate template, int count) =>
        template == ZoneTemplate.None ? 1 : Math.Clamp(count, MinimumZones, MaximumZones);

    /// <summary>La zona que más se solapa con una ventana, o -1 si ninguna la toca.</summary>
    /// <remarks>
    /// Sirve para saber en qué zona está ya una ventana, que es lo que permite que un atajo de
    /// "zona siguiente" sepa desde dónde parte.
    /// </remarks>
    public static int ZoneAt(IReadOnlyList<WindowBounds> zones, WindowBounds window)
    {
        ArgumentNullException.ThrowIfNull(zones);

        int best = -1;
        long bestOverlap = 0;

        for (int i = 0; i < zones.Count; i++)
        {
            long overlap = Overlap(zones[i], window);

            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = i;
            }
        }

        return best;
    }

    /// <summary>La zona bajo un punto, o -1. Es lo que decide dónde cae una ventana al soltarla.</summary>
    public static int ZoneUnder(IReadOnlyList<WindowBounds> zones, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(zones);

        for (int i = 0; i < zones.Count; i++)
        {
            if (x >= zones[i].X && x < zones[i].Right && y >= zones[i].Y && y < zones[i].Bottom)
            {
                return i;
            }
        }

        return -1;
    }

    private static long Overlap(WindowBounds a, WindowBounds b)
    {
        long width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        long height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);

        return width <= 0 || height <= 0 ? 0 : width * height;
    }

    /// <summary>
    /// Reparte en columnas por filas repartiendo los píxeles sobrantes.
    /// </summary>
    /// <remarks>
    /// El reparto del resto importa: dividir 3441 entre tres y multiplicar deja un píxel de
    /// monitor sin cubrir por el borde derecho, y esa raya se ve.
    /// </remarks>
    private static List<WindowBounds> Split(WindowBounds area, int columns, int rows)
    {
        List<WindowBounds> zones = new();

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int x = area.X + (area.Width * column / columns);
                int right = area.X + (area.Width * (column + 1) / columns);
                int y = area.Y + (area.Height * row / rows);
                int bottom = area.Y + (area.Height * (row + 1) / rows);

                zones.Add(new WindowBounds(x, y, right - x, bottom - y));
            }
        }

        return zones;
    }

    /// <summary>
    /// La cuadrícula más cuadrada para ese número de zonas.
    /// </summary>
    /// <remarks>
    /// Las columnas salen de la raíz cuadrada redondeada hacia arriba, que da 2x2 para cuatro,
    /// 3x2 para cinco y seis, y 3x3 para nueve. Con cinco sobra un hueco y la última zona se
    /// ensancha para ocuparlo, porque un agujero en una esquina de la pantalla no es una zona,
    /// es una zona que falta.
    /// </remarks>
    private static List<WindowBounds> Grid(WindowBounds area, int count)
    {
        int columns = (int)Math.Ceiling(Math.Sqrt(count));
        int rows = (int)Math.Ceiling(count / (double)columns);

        List<WindowBounds> all = Split(area, columns, rows);

        if (all.Count == count)
        {
            return all;
        }

        List<WindowBounds> zones = all.Take(count).ToList();

        // La última se estira hasta el borde derecho para tapar los huecos de su fila.
        WindowBounds last = zones[^1];
        zones[^1] = last with { Width = area.Right - last.X };

        return zones;
    }

    /// <summary>Una zona grande a un lado y el resto apiladas al otro.</summary>
    private static List<WindowBounds> Priority(WindowBounds area, int count, bool bigOnLeft)
    {
        if (count <= 1)
        {
            return new List<WindowBounds> { area };
        }

        // Dos tercios para la grande: es la proporción con la que un editor a la izquierda y un
        // navegador y una terminal a la derecha quedan los tres usables.
        int bigWidth = Math.Max(MinimumZoneSize, area.Width * 2 / 3);
        int smallWidth = area.Width - bigWidth;

        WindowBounds big = bigOnLeft
            ? new WindowBounds(area.X, area.Y, bigWidth, area.Height)
            : new WindowBounds(area.X + smallWidth, area.Y, bigWidth, area.Height);

        int stackX = bigOnLeft ? area.X + bigWidth : area.X;

        List<WindowBounds> zones = new() { big };
        int stacked = count - 1;

        for (int i = 0; i < stacked; i++)
        {
            int y = area.Y + (area.Height * i / stacked);
            int bottom = area.Y + (area.Height * (i + 1) / stacked);

            zones.Add(new WindowBounds(stackX, y, smallWidth, bottom - y));
        }

        return zones;
    }

    /// <summary>
    /// Mete la zona hacia dentro medio hueco por cada lado.
    /// </summary>
    /// <remarks>
    /// Medio por lado y no uno entero, para que entre dos zonas contiguas quede el hueco pedido y
    /// no el doble. Y nunca se encoge tanto como para dejarla inservible.
    /// </remarks>
    private static WindowBounds Shrink(WindowBounds zone, int gap)
    {
        int half = gap / 2;

        int width = zone.Width - (half * 2);
        int height = zone.Height - (half * 2);

        if (width < MinimumZoneSize || height < MinimumZoneSize)
        {
            return zone;
        }

        return new WindowBounds(zone.X + half, zone.Y + half, width, height);
    }
}
