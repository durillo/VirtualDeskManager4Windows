using System;
using System.Windows.Threading;
using VirtualDeskManager.App.Tray;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Events;

namespace VirtualDeskManager.App;

/// <summary>
/// Mantiene el icono de la bandeja diciendo en qué escritorio estás.
/// </summary>
/// <remarks>
/// En la bandeja y no en la barra de tareas porque Windows 11 no deja meter nada en la barra: se
/// llevó por delante las <i>deskbands</i> y no dejó ningún punto de extensión. Lo que hay por ahí
/// que lo consigue, inyecta un DLL en el explorador, y este programa no hace eso.
///
/// El cambio de escritorio se detecta de dos maneras a la vez, y hacen falta las dos. El evento
/// de primer plano llega en el acto y cubre el caso normal, porque cambiar de escritorio casi
/// siempre pone otra ventana delante; pero al saltar a un escritorio vacío no hay ventana que
/// pasar a primer plano y no llega nada. Para eso está el temporizador, que va lento a propósito:
/// es una red de seguridad, no el mecanismo principal.
/// </remarks>
public sealed class DesktopIndicatorService : IDisposable
{
    /// <summary>
    /// Cada cuánto se comprueba por si acaso.
    /// </summary>
    /// <remarks>
    /// Un segundo. Preguntar el escritorio actual es una llamada COM barata, pero esto corre
    /// mientras la aplicación esté viva, así que tampoco hay que abusar. El caso que cubre
    /// —saltar a un escritorio vacío— no es tan frecuente como para pedir más finura.
    /// </remarks>
    private static readonly TimeSpan Backstop = TimeSpan.FromSeconds(1);

    private readonly IVirtualDesktopService _desktops;
    private readonly WinEventWindowEventService _windowEvents;
    private readonly TrayService _tray;
    private readonly Func<bool> _enabled;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;

    private Guid _shown = Guid.Empty;
    private bool _disposed;

    /// <summary>Se engancha al evento de primer plano y prepara el temporizador, pero no lo arranca.</summary>
    /// <param name="dispatcher">El hilo de la interfaz, que es el dueño del icono.</param>
    /// <param name="desktops">De dónde se saca el escritorio actual.</param>
    /// <param name="windowEvents">Avisa de los cambios de ventana en primer plano.</param>
    /// <param name="tray">El icono que se actualiza.</param>
    /// <param name="enabled">Si el usuario quiere ver el escritorio en la bandeja; se consulta en cada repaso.</param>
    /// <param name="logger">Dónde apuntar los fallos.</param>
    public DesktopIndicatorService(
        Dispatcher dispatcher,
        IVirtualDesktopService desktops,
        WinEventWindowEventService windowEvents,
        TrayService tray,
        Func<bool> enabled,
        IAppLogger? logger = null)
    {
        _dispatcher = dispatcher;
        _desktops = desktops;
        _windowEvents = windowEvents;
        _tray = tray;
        _enabled = enabled;
        _logger = logger ?? NullAppLogger.Instance;

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Backstop };
        _timer.Tick += (_, _) => Refresh();

        _windowEvents.ForegroundWindowChanged += OnForegroundChanged;
    }

    /// <summary>Arranca la comprobación periódica y pinta el icono ya, sin esperar al primer latido.</summary>
    public void Start()
    {
        _timer.Start();
        Refresh();
    }

    /// <summary>
    /// Vuelve a mirar en qué escritorio estamos y actualiza el icono si ha cambiado.
    /// </summary>
    /// <remarks>
    /// Público para que los ajustes puedan pedir un repaso en cuanto se activa o se desactiva la
    /// opción, sin esperar al siguiente latido.
    /// </remarks>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_enabled())
            {
                if (_shown != Guid.Empty)
                {
                    _shown = Guid.Empty;
                    _tray.ClearDesktop();
                }

                return;
            }

            VirtualDesktopInfo? current = _desktops.GetCurrentDesktop();

            if (current is null)
            {
                // El backend no sabe decirlo: mejor el icono de siempre que un número inventado.
                if (_shown != Guid.Empty)
                {
                    _shown = Guid.Empty;
                    _tray.ClearDesktop();
                }

                return;
            }

            // El nombre también se refresca aunque el escritorio sea el mismo, porque el usuario
            // puede haberlo cambiado desde la pestaña de escritorios.
            _shown = current.Id;
            _tray.UpdateDesktop(current.Number, current.HasName ? current.Name : "");
        }
        catch (Exception ex)
        {
            // Un indicador no es motivo para tumbar nada. Se apunta y se sigue.
            _logger.Debug(LogEvents.NativeApiError, ("Where", nameof(DesktopIndicatorService)), ("Error", ex.Message));
        }
    }

    private void OnForegroundChanged(object? sender, WindowDetectedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // Llega en el hilo del bucle de mensajes; el icono es de la interfaz.
        _dispatcher.BeginInvoke(new Action(Refresh));
    }

    /// <summary>Se suelta del evento de primer plano y para el temporizador. El icono se queda como esté.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _windowEvents.ForegroundWindowChanged -= OnForegroundChanged;
        _timer.Stop();
    }
}
