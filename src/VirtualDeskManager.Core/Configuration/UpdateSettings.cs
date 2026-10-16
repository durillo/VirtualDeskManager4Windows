namespace VirtualDeskManager.Core.Configuration;

/// <summary>
/// La actualización por internet (decidida con el usuario el 2026-10-09): Vdesk busca solo, avisa
/// y pregunta antes de instalar nada.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>Buscar sola al arrancar y una vez al día. Encendido de serie.</summary>
    public bool CheckAutomatically { get; set; } = true;

    /// <summary>
    /// Instalar sola la versión nueva, sin preguntar (pedido el 2026-10-10). Se baja en cuanto se
    /// ve y se aplica cuando el usuario no está usando el equipo (<c>AutoUpdatePolicy</c>).
    /// Apagado de serie: quien no lo enciende sigue con el aviso y la pregunta. Sólo en la Vdesk
    /// del instalador; el .exe suelto no se puede actualizar en su sitio.
    /// </summary>
    public bool InstallAutomatically { get; set; }

    /// <summary>
    /// La versión que se instaló sola, para decir al volver a abrir «se ha actualizado a…». Vacía
    /// cuando ya se ha dicho.
    /// </summary>
    public string InstalledAutomatically { get; set; } = "";

    /// <summary>
    /// Recibir también las versiones de prueba. Apagado de serie: sólo le llegan a quien lo pide.
    /// </summary>
    public bool Beta { get; set; }

    /// <summary>
    /// La dirección del servidor. Vacía: la de serie (<c>UpdateKeys.DefaultServer</c>). Sólo para
    /// probar con otro servidor: la firma se comprueba igual.
    /// </summary>
    public string Server { get; set; } = "";

    /// <summary>La última versión de la que ya se avisó, para no repetir el aviso cada día.</summary>
    public string LastAnnounced { get; set; } = "";
}
