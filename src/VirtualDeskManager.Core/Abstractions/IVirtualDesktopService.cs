using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>
/// The single door to the virtual desktop subsystem (spec section 12).
/// COM interfaces and CLSIDs must never leak past an implementation of this interface.
/// </summary>
/// <remarks>
/// Everything past the first three members depends on the backend in use, so callers must consult
/// <see cref="Capabilities"/> first. Whatever is unavailable answers
/// <see cref="OperationStatus.Unsupported"/> instead of throwing (spec section 14).
/// </remarks>
public interface IVirtualDesktopService
{
    /// <summary>Backend shown in the dashboard, e.g. "Public Windows API".</summary>
    string BackendName { get; }

    /// <summary>What the backend in use can do. Check it before calling the optional members.</summary>
    VirtualDesktopCapabilities Capabilities { get; }

    /// <summary>
    /// Whether the window is on the desktop being shown. When the backend cannot tell, the answer
    /// is true: it is the one that never triggers a move.
    /// </summary>
    bool IsWindowOnCurrentDesktop(nint hwnd);

    /// <summary>
    /// Desktop the window lives on, or null when it lives on no single one: either the backend
    /// cannot tell, or the window is pinned and is therefore on every desktop.
    /// </summary>
    Guid? GetWindowDesktopId(nint hwnd);

    /// <summary>Sends a window to another desktop without switching the view.</summary>
    OperationResult MoveWindowToDesktop(nint hwnd, Guid desktopId);

    /// <summary>Desktops in the order Task View shows them. Empty when they cannot be enumerated.</summary>
    IReadOnlyList<VirtualDesktopInfo> GetDesktops();

    /// <summary>The desktop being shown, or null when the backend cannot tell.</summary>
    VirtualDesktopInfo? GetCurrentDesktop();

    /// <summary>Shows the given desktop.</summary>
    OperationResult SwitchToDesktop(Guid desktopId);

    /// <summary>
    /// Adds one desktop at the end and returns it, or null when it could not be confirmed.
    /// </summary>
    /// <remarks>
    /// Windows switches the view to the desktop it has just created. Whoever calls this while
    /// restoring a layout has to put the user back where they were.
    /// </remarks>
    VirtualDesktopInfo? CreateDesktop();

    /// <summary>
    /// Elimina un escritorio. Sus ventanas no se cierran: pasan al de al lado, como cuando se cierra
    /// un escritorio desde la vista de tareas de Windows.
    /// </summary>
    /// <remarks>
    /// Falla, sin tocar nada, si es el único escritorio o si no existe. Quien lo llama sigue en el
    /// escritorio en el que estaba, salvo que fuera precisamente el eliminado.
    /// </remarks>
    OperationResult RemoveDesktop(Guid desktopId);

    /// <summary>Renames a desktop. An empty name leaves it with the one Windows gives it.</summary>
    OperationResult SetDesktopName(Guid desktopId, string name);

    /// <summary>Null when the backend cannot tell.</summary>
    bool? IsWindowPinned(nint hwnd);

    /// <summary>Shows or stops showing one window on every desktop (spec section 18).</summary>
    OperationResult SetWindowPinned(nint hwnd, bool pinned);

    /// <summary>Null when the backend cannot tell.</summary>
    bool? IsApplicationPinned(nint hwnd);

    /// <summary>
    /// Shows or stops showing every window of an application on every desktop.
    /// </summary>
    /// <remarks>
    /// Not the same as pinning each of its windows, and the difference is the whole point: pinning
    /// windows only reaches the ones that exist right now. Windows remembers a pinned
    /// <i>application</i> and applies it to the windows it opens later — a Teams call, a second
    /// document — which is what someone means by "I want to see this everywhere".
    /// </remarks>
    OperationResult SetApplicationPinned(nint hwnd, bool pinned);
}
