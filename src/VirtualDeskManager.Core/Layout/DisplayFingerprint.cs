using System.Text;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Layout;

/// <summary>
/// Huella de una configuración de pantallas: cuántas hay, de qué tamaño y dónde.
/// </summary>
/// <remarks>
/// Es la pieza que hace que recordar posiciones funcione de verdad. El problema no es guardar
/// dónde estaban las ventanas, sino <b>cuándo</b> guardarlo: cuando el equipo suspende y las
/// pantallas se apagan, Windows amontona todas las ventanas en la principal, y si en ese momento
/// se guarda, lo que queda grabado es el destrozo.
///
/// Con una huella por configuración eso deja de poder pasar. Cada distribución de ventanas se
/// guarda asociada a las pantallas que había cuando se tomó, así que el amontonamiento se guarda
/// bajo la huella de "sólo el portátil" y la distribución buena sigue intacta bajo la de "portátil
/// más dos monitores". Al volver las pantallas, la huella vuelve a ser la de antes y con ella la
/// distribución correcta.
/// </remarks>
public readonly record struct DisplayFingerprint(string Value)
{
    /// <summary>Huella vacía: no se sabe qué pantallas hay. Con ella no se guarda ni se busca ninguna distribución.</summary>
    public static readonly DisplayFingerprint Unknown = new("");

    /// <summary>Si la huella describe pantallas reales y no es <see cref="Unknown"/>.</summary>
    public bool IsKnown => !string.IsNullOrEmpty(Value);

    /// <summary>
    /// Calcula la huella de un conjunto de monitores.
    /// </summary>
    /// <remarks>
    /// Entra la posición y el tamaño de cada monitor y cuál es el principal, y no entra el número
    /// que les da Windows: ese número cambia de orden al reconectar los cables y haría que la
    /// misma configuración física pareciera dos distintas.
    ///
    /// Se ordena por posición para que la huella no dependa del orden en que Windows los
    /// enumere, que tampoco es estable.
    /// </remarks>
    public static DisplayFingerprint Of(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        List<MonitorInfo> ordered = monitors
            .OrderBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .ToList();

        if (ordered.Count == 0)
        {
            return Unknown;
        }

        StringBuilder builder = new();

        foreach (MonitorInfo monitor in ordered)
        {
            if (builder.Length > 0)
            {
                builder.Append('|');
            }

            builder
                .Append(monitor.Bounds.Width)
                .Append('x')
                .Append(monitor.Bounds.Height)
                .Append('@')
                .Append(monitor.Bounds.X)
                .Append(',')
                .Append(monitor.Bounds.Y);

            if (monitor.IsPrimary)
            {
                builder.Append('*');
            }
        }

        return new DisplayFingerprint(builder.ToString());
    }

    /// <summary>El texto de la huella, por ejemplo <c>1920x1080@0,0*|2560x1440@1920,0</c>.</summary>
    public override string ToString() => Value;
}
