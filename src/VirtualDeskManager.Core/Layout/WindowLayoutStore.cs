using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Layout;

/// <summary>
/// Empareja las ventanas de ahora con las posiciones guardadas, y guarda una distribución por
/// configuración de pantallas.
/// </summary>
/// <remarks>
/// Aritmética y emparejamiento puros, sin Win32, para poder probarlo (sección 40). Quien mueva
/// las ventanas de verdad es la aplicación.
/// </remarks>
public static class WindowLayoutStore
{
    /// <summary>Cuántas configuraciones de pantallas distintas se recuerdan.</summary>
    /// <remarks>
    /// Ocho cubre de sobra a quien va con el portátil entre la oficina, casa y una sala de
    /// reuniones. Más allá de eso lo que hay es un historial que ya no describe ningún sitio real.
    /// </remarks>
    public const int MaximumRememberedLayouts = 8;

    /// <summary>
    /// Qué ventanas de las que hay ahora hay que devolver a su sitio, y a cuál.
    /// </summary>
    /// <remarks>
    /// El emparejamiento va en tres pasadas, de lo más específico a lo más general: primero las
    /// que coinciden en proceso, clase y título; después en proceso y clase; y por último sólo en
    /// proceso. Hace falta así porque el título cambia solo —un editor pone el nombre del fichero
    /// abierto, un navegador el de la pestaña— y exigir que coincida dejaría sin recolocar
    /// justamente las ventanas en las que uno estaba trabajando. Ir de lo específico a lo general
    /// hace que, con tres ventanas del mismo programa, cada una acabe donde estaba en vez de
    /// repartirse al azar.
    ///
    /// Cada ventana se usa una vez y cada posición se usa una vez: si había tres ventanas de un
    /// programa y ahora hay dos, se colocan dos y la tercera posición se queda sin usar.
    /// </remarks>
    public static IReadOnlyList<PlacementRestore> Match(
        WindowLayoutSnapshot snapshot,
        IReadOnlyList<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(windows);

        List<PlacementRestore> restores = new();
        HashSet<nint> usedWindows = new();
        HashSet<WindowPlacement> usedPlacements = new();

        // Tres pasadas, de más exigente a menos.
        Func<WindowInfo, WindowPlacement, bool>[] passes =
        {
            (window, placement) =>
                SameProcess(window, placement)
                && SameClass(window, placement)
                && string.Equals(window.Title, placement.Title, StringComparison.Ordinal),

            (window, placement) => SameProcess(window, placement) && SameClass(window, placement),

            SameProcess
        };

        foreach (Func<WindowInfo, WindowPlacement, bool> matches in passes)
        {
            foreach (WindowPlacement placement in snapshot.Placements)
            {
                if (usedPlacements.Contains(placement))
                {
                    continue;
                }

                WindowInfo? window = windows.FirstOrDefault(
                    candidate => !usedWindows.Contains(candidate.Handle)
                                 && IsWorthMoving(candidate, placement)
                                 && matches(candidate, placement));

                if (window is null)
                {
                    continue;
                }

                usedWindows.Add(window.Handle);
                usedPlacements.Add(placement);
                restores.Add(new PlacementRestore(window.Handle, placement));
            }
        }

        return restores;
    }

    /// <summary>
    /// Si merece la pena tocar esta ventana.
    /// </summary>
    /// <remarks>
    /// Una ventana minimizada se deja en paz aunque su posición guardada diga otra cosa. Después
    /// de una suspensión las ventanas no vuelven minimizadas, vuelven amontonadas, así que si una
    /// está minimizada es porque el usuario la minimizó él: sacársela de golpe a la cara sería
    /// justo lo contrario de lo que espera.
    ///
    /// Y una que ya esté donde tiene que estar no se toca, para no generar eventos de movimiento
    /// que disparen el resto de la aplicación sin que nada haya cambiado.
    /// </remarks>
    private static bool IsWorthMoving(WindowInfo window, WindowPlacement placement)
    {
        if (window.IsMinimized)
        {
            return false;
        }

        bool sameBounds = window.Bounds == placement.Bounds;
        bool sameState = CurrentState(window) == placement.State;

        return !sameBounds || !sameState;
    }

    /// <summary>Estado visible de una ventana: minimizada, maximizada o normal, en ese orden de prioridad.</summary>
    public static WindowVisualState CurrentState(WindowInfo window) => window switch
    {
        { IsMinimized: true } => WindowVisualState.Minimized,
        { IsMaximized: true } => WindowVisualState.Maximized,
        _ => WindowVisualState.Normal
    };

    private static bool SameProcess(WindowInfo window, WindowPlacement placement) =>
        string.Equals(window.ProcessName, placement.Process, StringComparison.OrdinalIgnoreCase);

    private static bool SameClass(WindowInfo window, WindowPlacement placement) =>
        string.Equals(window.ClassName, placement.ClassName, StringComparison.Ordinal);

    /// <summary>
    /// Guarda una distribución y devuelve la lista entera, con las más viejas podadas.
    /// </summary>
    /// <remarks>
    /// Una distribución por huella: volver a guardar con la misma configuración de pantallas
    /// sustituye la anterior en vez de acumular. Lo que interesa de un sitio es cómo estaba la
    /// última vez que se estuvo en él, no todas las veces.
    /// </remarks>
    public static IReadOnlyList<WindowLayoutSnapshot> Remember(
        IEnumerable<WindowLayoutSnapshot> existing,
        WindowLayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(snapshot);

        List<WindowLayoutSnapshot> result = existing
            .Where(other => other.Fingerprint != snapshot.Fingerprint)
            .ToList();

        result.Add(snapshot);

        return result
            .OrderByDescending(one => one.Taken)
            .Take(MaximumRememberedLayouts)
            .ToList();
    }

    /// <summary>
    /// La distribución guardada para esta configuración de pantallas, o <c>null</c> si no hay
    /// ninguna o la huella es <see cref="DisplayFingerprint.Unknown"/>.
    /// </summary>
    public static WindowLayoutSnapshot? Find(
        IEnumerable<WindowLayoutSnapshot> snapshots,
        DisplayFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return fingerprint.IsKnown
            ? snapshots.FirstOrDefault(snapshot => snapshot.Fingerprint == fingerprint)
            : null;
    }
}
