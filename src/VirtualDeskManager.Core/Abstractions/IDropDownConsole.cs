using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>
/// A shell that slides down from the top of the screen on a keystroke and rolls back up when it
/// stops being needed, in the manner of the Quake console.
/// </summary>
/// <remarks>
/// The point of it is that it is never started, only uncovered: the shell stays alive behind the
/// top edge of the screen with its history and whatever was running still there, so summoning it
/// costs nothing and dismissing it loses nothing.
/// </remarks>
public interface IDropDownConsole
{
    /// <summary>True while the console is down, including while it is still sliding.</summary>
    bool IsOpen { get; }

    /// <summary>Rolls it down, starting the shell if this is the first time.</summary>
    OperationResult Show();

    /// <summary>Rolls it back up, leaving the shell running.</summary>
    OperationResult Hide();

    /// <summary>Down when it is up and up when it is down.</summary>
    OperationResult Toggle();

    /// <summary>
    /// Fijada: no se recoge sola al pasar a otra ventana. Su atajo la sigue recogiendo, y sigue
    /// fijada la próxima vez que baje.
    /// </summary>
    bool IsPinned { get; }

    /// <summary>La fija si está suelta y la suelta si está fijada.</summary>
    OperationResult TogglePin();
}
