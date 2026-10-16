using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>
/// The overview Windows shows of every desktop and every window at once, what the shell calls
/// Task View (spec section 17).
/// </summary>
/// <remarks>
/// Behind an interface like everything else so that Core never learns what a keystroke is. The
/// only implementation drives it the way a person would, because Windows exposes no supported
/// way to open Task View: there is no documented API, and the internal one lives in the same
/// stretch of undocumented table this project refuses to guess at.
/// </remarks>
public interface ITaskViewService
{
    /// <summary>True while the overview is on screen and holding the foreground.</summary>
    bool IsOpen { get; }

    /// <summary>Opens it. Already open is a success, not an error.</summary>
    OperationResult Show();

    /// <summary>Closes it and gives the foreground back to whatever had it.</summary>
    OperationResult Hide();
}
