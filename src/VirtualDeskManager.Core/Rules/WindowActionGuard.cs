namespace VirtualDeskManager.Core.Rules;

/// <summary>
/// Hace que una regla se aplique <b>una vez por ventana</b>, y no una y otra vez.
/// </summary>
/// <remarks>
/// Nació como simple protección contra bucles (sección 23): aplicar una acción genera eventos de
/// Windows que vuelven a disparar la regla, así que se ignoraban las repeticiones durante dos
/// segundos.
///
/// Dos segundos resultaron ser lo peor de los dos mundos. Bastaban para no morderse la cola, pero
/// convertían cada regla en una imposición permanente: como las reglas se reevalúan al cambiar el
/// foco, una que dijera "maximizada" volvía a maximizar la ventana cada vez que el usuario la
/// pulsaba, y otra que dijera "no fijada" la desfijaba tres milisegundos después de que él la
/// fijara a mano. Medido en el equipo del usuario: 89 maximizaciones de la misma aplicación en
/// una mañana, y una ventana que no había forma de dejar pequeña.
///
/// Ahora una regla se aplica cuando la ventana aparece y se calla. Si después el usuario la mueve,
/// la redimensiona o la desfija, ésa es su decisión y no se le discute. Cambiar la regla cambia la
/// firma, y entonces sí vuelve a aplicarse.
/// </remarks>
public sealed class WindowActionGuard
{
    private const int MaxTrackedWindows = 512;

    private readonly object _gate = new();
    private readonly Dictionary<nint, Entry> _entries = new();
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Crea el freno. El reloj sólo se cambia en las pruebas.</summary>
    public WindowActionGuard(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Returns true when the caller may apply <paramref name="signature"/> to
    /// <paramref name="hwnd"/>. The caller must then call <see cref="End"/> exactly once.
    /// </summary>
    public bool TryBegin(nint hwnd, string signature)
    {
        DateTimeOffset now = _clock();

        lock (_gate)
        {
            if (_entries.TryGetValue(hwnd, out Entry? entry))
            {
                if (entry.InFlight)
                {
                    return false;
                }

                // Sin plazo: si a esta ventana ya se le aplicó esto, no se le vuelve a aplicar
                // mientras exista. Lo que el usuario haga con ella después es cosa suya.
                if (string.Equals(entry.Signature, signature, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            else
            {
                PruneIfNeeded(now);
                entry = new Entry();
                _entries[hwnd] = entry;
            }

            entry.InFlight = true;
            entry.Signature = signature;
            entry.AppliedAt = now;
            return true;
        }
    }

    /// <summary>
    /// Cierra lo abierto con <see cref="TryBegin"/>. La firma se queda apuntada, así que la misma
    /// no se vuelve a aplicar a esa ventana.
    /// </summary>
    public void End(nint hwnd)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(hwnd, out Entry? entry))
            {
                entry.InFlight = false;
                entry.AppliedAt = _clock();
            }
        }
    }

    /// <summary>Called when a window is destroyed so the HWND is not remembered after recycling.</summary>
    public void Forget(nint hwnd)
    {
        lock (_gate)
        {
            _entries.Remove(hwnd);
        }
    }

    /// <summary>Olvida todas las ventanas: después, cada regla se puede volver a aplicar a todas.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void PruneIfNeeded(DateTimeOffset now)
    {
        if (_entries.Count < MaxTrackedWindows)
        {
            return;
        }

        List<nint> stale = _entries
            .Where(pair => !pair.Value.InFlight && now - pair.Value.AppliedAt > TimeSpan.FromMinutes(5))
            .Select(pair => pair.Key)
            .ToList();

        foreach (nint handle in stale)
        {
            _entries.Remove(handle);
        }

        if (_entries.Count < MaxTrackedWindows)
        {
            return;
        }

        // Still full: drop the oldest half so the dictionary can never grow without bound.
        List<nint> oldest = _entries
            .Where(pair => !pair.Value.InFlight)
            .OrderBy(pair => pair.Value.AppliedAt)
            .Take(MaxTrackedWindows / 2)
            .Select(pair => pair.Key)
            .ToList();

        foreach (nint handle in oldest)
        {
            _entries.Remove(handle);
        }
    }

    private sealed class Entry
    {
        public string Signature { get; set; } = "";

        public DateTimeOffset AppliedAt { get; set; }

        public bool InFlight { get; set; }
    }
}
