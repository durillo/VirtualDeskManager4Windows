namespace VirtualDeskManager.Core.Updates;

/// <summary>
/// Qué hay que bajarse para pasar a una versión: sólo los ficheros que faltan o han cambiado.
/// </summary>
public static class UpdatePlan
{
    /// <summary>
    /// Los ficheros del manifiesto que aquí no están o tienen otra huella.
    /// </summary>
    /// <param name="manifest">La versión a la que se va, ya comprobada su firma.</param>
    /// <param name="localSha256">La huella de un fichero de aquí, por su ruta relativa; nula si no está.</param>
    /// <exception cref="InvalidDataException">Si alguna ruta se sale de la carpeta de la aplicación.</exception>
    public static IReadOnlyList<UpdateFile> FilesToDownload(UpdateManifest manifest, Func<string, string?> localSha256)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(localSha256);

        List<UpdateFile> faltan = new();

        foreach (UpdateFile f in manifest.Files)
        {
            if (!IsSafePath(f.Path) || !IsSha256(f.Sha256))
            {
                throw new InvalidDataException($"El manifiesto trae una ruta o una huella que no valen: «{f.Path}».");
            }

            if (!string.Equals(localSha256(f.Path), f.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                faltan.Add(f);
            }
        }

        return faltan;
    }

    /// <summary>
    /// Que una ruta del manifiesto se queda dentro de la carpeta de la aplicación: relativa, sin
    /// «..», sin unidad y sin nada raro. Aunque el manifiesto venga firmado, no se escribe fuera.
    /// </summary>
    public static bool IsSafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 260)
        {
            return false;
        }

        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':') || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return false;
        }

        return path.Split('/', '\\').All(parte => parte.Length > 0 && parte != "." && parte != "..");
    }

    /// <summary>Que sea una huella SHA-256 en hexadecimal.</summary>
    public static bool IsSha256(string? text) =>
        text is { Length: 64 } && text.All(Uri.IsHexDigit);
}
