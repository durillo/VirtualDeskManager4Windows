using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Abstractions;

/// <summary>Datos de <see cref="IActiveWindowTracker.AnchorChanged"/>: dónde va ahora el botón de la barra de título.</summary>
public sealed class TitleBarAnchorEventArgs : EventArgs
{
    /// <summary>Crea el aviso con el ancla nueva, o con null para esconder el botón.</summary>
    public TitleBarAnchorEventArgs(TitleBarAnchor? anchor) => Anchor = anchor;

    /// <summary>Null means "there is nowhere to put the button": hide it.</summary>
    public TitleBarAnchor? Anchor { get; }
}

/// <summary>
/// Follows the active window and says where its title bar button should sit.
/// </summary>
/// <remarks>
/// Event driven, like everything else: it listens to foreground, move, resize, minimize and
/// destroy events instead of polling window positions (architecture rule 8).
/// </remarks>
public interface IActiveWindowTracker
{
    /// <summary>Si los ganchos de eventos están puestos, es decir, si se ha llamado a <see cref="Start"/> sin un <see cref="Stop"/> después.</summary>
    bool IsRunning { get; }

    /// <summary>Extra pixels reserved on the right for applications that draw their own caption.</summary>
    int ExtraOffset { get; set; }

    /// <summary>Corrección de tamaño del botón, en porcentaje del natural.</summary>
    int SizePercent { get; set; }

    /// <summary>
    /// Avisa de que el botón tiene que moverse o esconderse. Sólo salta cuando el ancla cambia de
    /// verdad, no con cada evento de posición mientras se arrastra la ventana.
    /// </summary>
    event EventHandler<TitleBarAnchorEventArgs>? AnchorChanged;

    /// <summary>Empieza a seguir la ventana activa poniendo los ganchos de eventos de Windows.</summary>
    void Start();

    /// <summary>Quita los ganchos y deja de publicar anclas.</summary>
    void Stop();

    /// <summary>Recomputes the anchor for the window that is in front right now.</summary>
    void Refresh();
}
