using VirtualDeskManager.Core.Configuration;

namespace VirtualDeskManager.Core.Taskbar;

/// <summary>Lo que se pinta encima de un botón de la barra de tareas.</summary>
/// <param name="Text">El texto del distintivo, vacío cuando no hay que pintar nada.</param>
public readonly record struct TaskbarBadge(string Text)
{
    /// <summary>Sin distintivo: no se pinta nada.</summary>
    public static readonly TaskbarBadge None = new(string.Empty);

    /// <summary>Si hay algo que pintar.</summary>
    public bool ShouldShow => !string.IsNullOrEmpty(Text);
}

/// <summary>
/// Qué distintivo le toca a una aplicación según dónde tenga sus ventanas.
/// </summary>
/// <remarks>
/// Sin Win32 por medio, que es lo que permite probarlo. Lo difícil de esta función no es pintar
/// un número —eso se ve enseguida si sale mal— sino decidir <b>qué</b> número cuando una misma
/// aplicación tiene ventanas repartidas por varios escritorios, que es el caso normal y no el
/// raro.
///
/// Los glifos son deliberadamente <b>no traducibles</b>: en un cuadrado de catorce píxeles no
/// cabe una palabra, y una letra inicial que funcione en español no funcionaría en árabe. Un
/// número es un número en todas partes.
/// </remarks>
public static class TaskbarBadgePolicy
{
    /// <summary>La aplicación está anclada: se ve en todos los escritorios.</summary>
    public const string Everywhere = "∞";

    /// <summary>Tiene ventanas en varios escritorios a la vez.</summary>
    public const string Several = "…";

    /// <summary>Más escritorios de los que caben en un dígito.</summary>
    public const string Beyond = "+";

    /// <summary>
    /// El distintivo de una aplicación.
    /// </summary>
    /// <param name="desktops">Escritorios, empezando en uno, donde tiene ventanas. Puede repetir.</param>
    /// <param name="pinned">Si alguna de sus ventanas está anclada a todos los escritorios.</param>
    /// <param name="current">El escritorio en el que está el usuario ahora, empezando en uno.</param>
    /// <param name="settings">Las preferencias del usuario; de aquí sale si se oculta el distintivo del escritorio actual. No puede ser nulo.</param>
    public static TaskbarBadge For(
        IReadOnlyList<int> desktops,
        bool pinned,
        int current,
        TaskbarBadgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(desktops);
        ArgumentNullException.ThrowIfNull(settings);

        // Anclada gana sobre todo lo demas: da igual en que escritorio se abriera, se ve en todos,
        // y un numero ahi seria mentira.
        if (pinned)
        {
            return new TaskbarBadge(Everywhere);
        }

        List<int> unicos = desktops.Where(n => n > 0).Distinct().OrderBy(n => n).ToList();

        if (unicos.Count == 0)
        {
            // Un boton anclado sin ninguna ventana abierta. No se sabe nada, asi que no se dice
            // nada: inventar un numero seria peor que no poner distintivo.
            return TaskbarBadge.None;
        }

        if (unicos.Count > 1)
        {
            return new TaskbarBadge(Several);
        }

        int unico = unicos[0];

        if (settings.HideWhenCurrent && unico == current)
        {
            return TaskbarBadge.None;
        }

        return new TaskbarBadge(unico <= 9 ? unico.ToString() : Beyond);
    }
}
