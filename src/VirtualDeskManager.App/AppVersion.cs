using System.Reflection;

namespace VirtualDeskManager.App;

/// <summary>
/// La versión tal como se le enseña al usuario: con la etapa si la hay ("0.22.0-beta.1"). La
/// numérica del ensamblado no la lleva, porque Windows Installer sólo entiende números.
/// </summary>
internal static class AppVersion
{
    /// <summary>El nombre del producto tal como se le enseña al usuario.</summary>
    public const string ProductName = "Virtual Desk Manager";

    public static string Display { get; } = Read();

    private static string Read()
    {
        Assembly ensamblado = typeof(AppVersion).Assembly;
        string? informativa = ensamblado.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informativa))
        {
            // El SDK le añade "+<commit>" detrás; eso es para el registro, no para el usuario.
            int mas = informativa.IndexOf('+');
            return mas < 0 ? informativa : informativa[..mas];
        }

        return ensamblado.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
