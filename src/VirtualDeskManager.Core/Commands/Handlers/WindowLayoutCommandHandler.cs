using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Commands.Handlers;

/// <summary>
/// Devuelve las ventanas a donde estaban con la configuración de pantallas actual.
/// </summary>
/// <remarks>
/// El comando estaba declarado desde la primera versión y no hacía nada: era un hueco de la
/// especificación esperando a que existiera algo que recordara las posiciones.
///
/// Recibe una función en vez del servicio entero porque ese servicio vive en la capa de
/// aplicación —necesita temporizadores y el bucle de mensajes— y Core no puede verlo.
/// </remarks>
public sealed class WindowLayoutCommandHandler : ICommandHandler
{
    /// <summary>Devuelve cuántas ventanas ha movido, o -1 si no hay nada guardado.</summary>
    private readonly Func<int> _restore;

    private readonly IAppLogger _logger;

    /// <summary>Crea el manejador.</summary>
    /// <param name="restore">Restaura la distribución guardada y devuelve cuántas ventanas movió, o -1 si no hay nada guardado.</param>
    /// <param name="logger">Diario; ninguno si es null.</param>
    public WindowLayoutCommandHandler(Func<int> restore, IAppLogger? logger = null)
    {
        _restore = restore;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<CommandType> SupportedTypes { get; } = new[] { CommandType.RestoreWindowLayout };

    /// <summary>
    /// Restaura la distribución. Si con estas pantallas no hay nada guardado, falla con un mensaje
    /// que lo explica en vez de dar la orden por cumplida.
    /// </summary>
    public Task<OperationResult> ExecuteAsync(
        CommandDefinition command,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        int moved = _restore();

        if (moved < 0)
        {
            // No es un fallo: es que con estas pantallas todavia no se ha guardado nada, y decirlo
            // asi evita que el usuario busque una averia donde solo falta esperar.
            return Task.FromResult(
                OperationResult.Fail("Todavia no hay ninguna distribucion guardada para estas pantallas."));
        }

        _logger.Info(LogEvents.CommandDispatched, ("Command", CommandType.RestoreWindowLayout), ("Moved", moved));

        return Task.FromResult(OperationResult.Ok());
    }
}
