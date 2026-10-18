namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// Enganche de la persistencia con las copias de seguridad.
/// </summary>
/// <remarks>
/// Una interfaz en vez de que <see cref="JsonConfigurationService"/> conozca al servicio de copias
/// directamente, porque la dependencia natural va al revés: las copias necesitan saber dónde están
/// los ficheros, y quien los escribe no tiene por qué saber que alguien los está guardando.
/// </remarks>
public interface IConfigurationHistory
{
    /// <summary>Justo antes de pisar un fichero, con el contenido anterior todavía en disco.</summary>
    void BeforeChange();

    /// <summary>Justo después, con el contenido nuevo ya escrito.</summary>
    void AfterChange();
}
