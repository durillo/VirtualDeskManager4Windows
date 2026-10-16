namespace VirtualDeskManager.Core.Updates;

/// <summary>
/// Una versión de Vdesk con su etapa: <c>0.24.0</c>, <c>0.24.0-beta.2</c>. Se ordenan como espera
/// cualquiera: <c>0.24.0-beta.1 &lt; 0.24.0-beta.2 &lt; 0.24.0 &lt; 0.24.1</c>.
/// </summary>
public sealed record UpdateVersion(Version Number, int? Beta) : IComparable<UpdateVersion>
{
    /// <summary>Lee <c>0.24.0</c> o <c>0.24.0-beta.2</c>; nulo si no es una versión.</summary>
    public static UpdateVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] partes = text.Trim().Split('-', 2);

        if (!Version.TryParse(partes[0], out Version? numero))
        {
            return null;
        }

        // 0.24.0.0 y 0.24.0 son la misma: el cuarto número no cuenta.
        numero = new Version(numero.Major, numero.Minor, Math.Max(0, numero.Build));

        if (partes.Length == 1)
        {
            return new UpdateVersion(numero, null);
        }

        string etapa = partes[1];
        return etapa.StartsWith("beta.", StringComparison.OrdinalIgnoreCase) && int.TryParse(etapa[5..], out int n)
            ? new UpdateVersion(numero, n)
            : null;
    }

    /// <inheritdoc/>
    public int CompareTo(UpdateVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int porNumero = Number.CompareTo(other.Number);

        if (porNumero != 0)
        {
            return porNumero;
        }

        // Con el mismo número, la definitiva va después de todas sus betas.
        return (Beta, other.Beta) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (a, b) => a!.Value.CompareTo(b!.Value)
        };
    }

    /// <summary>Si esta es más nueva que aquella.</summary>
    public bool IsNewerThan(UpdateVersion other) => CompareTo(other) > 0;

    /// <inheritdoc/>
    public override string ToString() => Beta is { } b ? $"{Number}-beta.{b}" : Number.ToString();
}
