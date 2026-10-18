using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>
/// The overview this application draws itself: one vertical zone per virtual desktop, every
/// window inside its zone as a live thumbnail, and windows moved between desktops by dragging.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ITaskViewService"/> on purpose. That one drives the overview the
/// shell already has, by pressing Win+Tab the way a person would; this one is ours, so it can
/// show every desktop side by side at once instead of one at a time, and it can be dragged
/// into. Both stay available: neither is a replacement for the other.
/// </remarks>
public interface IDesktopGridView
{
    /// <summary>True while the overview is on screen.</summary>
    bool IsOpen { get; }

    /// <summary>Opens it over everything else. Already open is a success, not an error.</summary>
    OperationResult Show();

    /// <summary>Closes it and gives the foreground back to whatever had it.</summary>
    OperationResult Hide();
}
