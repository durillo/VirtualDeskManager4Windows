namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// Recordar dónde estaba cada ventana y devolverla a su sitio.
/// </summary>
/// <remarks>
/// Existe por un fallo de Windows que no es de esta aplicación y que no se puede arreglar desde
/// fuera: cuando el equipo suspende o se bloquea, las pantallas se apagan, Windows se queda sólo
/// con la principal y amontona en ella todas las ventanas. Al volver, las pantallas están otra vez
/// pero las ventanas no: se quedan donde las dejó el amontonamiento.
/// </remarks>
public sealed class WindowLayoutSettings
{
    /// <summary>Si se recuerda la distribución de ventanas. Encendido de serie.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Recolocar solo al volver las pantallas o del reposo.</summary>
    public bool RestoreAutomatically { get; set; } = true;

    /// <summary>Recolocar también al arrancar la aplicación.</summary>
    /// <remarks>
    /// Aparte de lo anterior porque son dos casos distintos. Al volver de una suspensión las
    /// ventanas son las mismas y hay que rescatarlas; al arrancar, puede que el usuario acabe de
    /// encender el equipo y esté abriendo cosas, y moverle lo que abra sería entrometerse.
    /// </remarks>
    public bool RestoreOnStartup { get; set; }

    /// <summary>
    /// Cuánto se espera desde que cambian las pantallas hasta recolocar.
    /// </summary>
    /// <remarks>
    /// Windows no termina de reorganizarlo todo en el momento en que avisa del cambio: sigue
    /// moviendo ventanas durante un par de segundos. Recolocar antes de que acabe es tirar el
    /// trabajo, porque lo que venga después lo pisa.
    /// </remarks>
    public int RestoreDelayMilliseconds { get; set; } = 2500;

    /// <summary>Cada cuánto se guarda la distribución mientras todo está tranquilo.</summary>
    public int SaveIntervalSeconds { get; set; } = 30;

    /// <summary>Lleva la espera a 500–15 000 ms y el intervalo de guardado a 5–600 s.</summary>
    public void Normalise()
    {
        RestoreDelayMilliseconds = Math.Clamp(RestoreDelayMilliseconds, 500, 15000);
        SaveIntervalSeconds = Math.Clamp(SaveIntervalSeconds, 5, 600);
    }
}
