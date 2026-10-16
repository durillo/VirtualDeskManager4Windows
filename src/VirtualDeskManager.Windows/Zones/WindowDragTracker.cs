using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Windows.Messaging;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Zones;

/// <summary>Avisa de cuándo el usuario empieza y termina de arrastrar una ventana.</summary>
/// <remarks>
/// Con los eventos que el propio Windows emite y no vigilando el ratón: <c>MOVESIZESTART</c> y
/// <c>MOVESIZEEND</c> los manda el sistema cuando una ventana entra y sale del modo de arrastre,
/// así que llegan exactos y sin adivinar. Un gancho de ratón tendría que deducir lo mismo a partir
/// de pulsaciones y movimientos, y se equivocaría con las aplicaciones que se dibujan su propia
/// barra de título.
///
/// El evento también lo emite redimensionar, que para esto da igual: mientras la ventana está en
/// modo de arrastre o de redimensión, enseñar las zonas es igual de útil.
/// </remarks>
public sealed class WindowDragTracker : IDisposable
{
    private readonly Win32MessageLoop _loop;
    private readonly IAppLogger _logger;
    private readonly User32.WinEventProcDelegate _callback;

    private nint _hook;
    private bool _disposed;

    /// <summary>Lo prepara sin enganchar nada: el gancho se pone en <see cref="Start"/>.</summary>
    /// <param name="loop">Hilo del bucle de mensajes, donde se pone y se quita el gancho y desde donde se avisa.</param>
    /// <param name="logger">Diario de diagnóstico, opcional.</param>
    public WindowDragTracker(Win32MessageLoop loop, IAppLogger? logger = null)
    {
        _loop = loop;
        _logger = logger ?? NullAppLogger.Instance;

        // En un campo mientras el gancho exista: si lo recoge el basurero, Windows llama a una
        // funcion que ya no esta y el proceso se cae.
        _callback = OnWinEvent;
    }

    /// <summary>La ventana que se ha empezado a arrastrar.</summary>
    public event EventHandler<nint>? DragStarted;

    /// <summary>La ventana que se acaba de soltar.</summary>
    public event EventHandler<nint>? DragEnded;

    /// <summary>True mientras el gancho está puesto.</summary>
    public bool IsRunning => _hook != 0;

    /// <summary>Empieza a vigilar los arrastres. Si Windows no deja poner el gancho, lo apunta en el diario y sigue sin avisar.</summary>
    public void Start()
    {
        if (_disposed || IsRunning)
        {
            return;
        }

        _loop.Invoke(() =>
        {
            _hook = User32.SetWinEventHook(
                NativeConstants.EVENT_SYSTEM_MOVESIZESTART,
                NativeConstants.EVENT_SYSTEM_MOVESIZEEND,
                0,
                _callback,
                0,
                0,
                NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);
        });

        if (IsRunning)
        {
            _logger.Info("ZoneDragTrackerStarted");
        }
        else
        {
            _logger.Warn("ZoneDragTrackerUnavailable", "No se ha podido vigilar el arrastre de ventanas.");
        }
    }

    /// <summary>Quita el gancho y deja de avisar.</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _loop.Invoke(() =>
            {
                if (_hook != 0)
                {
                    User32.UnhookWinEvent(_hook);
                    _hook = 0;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Stop)));
            _hook = 0;
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        // Solo la ventana entera: el mismo evento llega tambien por controles de dentro.
        if (hwnd == 0
            || idObject != NativeConstants.OBJID_WINDOW
            || idChild != NativeConstants.CHILDID_SELF)
        {
            return;
        }

        if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZESTART)
        {
            DragStarted?.Invoke(this, hwnd);
        }
        else if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZEEND)
        {
            DragEnded?.Invoke(this, hwnd);
        }
    }

    /// <summary>Quita el gancho para siempre: después ya no se puede volver a arrancar.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
