using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Events;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.Core.Rules;

/// <summary>
/// Glue between window events and the rule engine: evaluates every relevant window and applies
/// the resolved actions through the services, never through Win32 directly
/// (architecture rules 3 and 4, spec section 47).
/// </summary>
public sealed class RuleApplicationService : IDisposable
{
    private readonly IRuleEngine _engine;
    private readonly IWindowManager _windows;
    private readonly IVirtualDesktopService _desktops;
    private readonly IWindowEventService _events;
    private readonly WindowActionGuard _guard;
    private readonly IAppLogger _logger;
    private readonly uint _ownProcessId;

    private bool _subscribed;
    private bool _disposed;

    /// <summary>Crea el servicio. No escucha nada hasta <see cref="Start"/>.</summary>
    /// <param name="engine">Motor que decide qué hacer con cada ventana.</param>
    /// <param name="windows">Por donde se mueven, maximizan y cierran las ventanas.</param>
    /// <param name="desktops">Por donde se cambian escritorios y anclajes.</param>
    /// <param name="events">Avisos de ventanas nuevas, mostradas, en primer plano y cerradas.</param>
    /// <param name="ownProcessId">Proceso de Vdesk: sus ventanas no se tocan nunca.</param>
    /// <param name="logger">Diario; si falta, no se registra nada.</param>
    /// <param name="guard">Freno contra bucles; se pasa en las pruebas.</param>
    public RuleApplicationService(
        IRuleEngine engine,
        IWindowManager windows,
        IVirtualDesktopService desktops,
        IWindowEventService events,
        uint ownProcessId,
        IAppLogger? logger = null,
        WindowActionGuard? guard = null)
    {
        _engine = engine;
        _windows = windows;
        _desktops = desktops;
        _events = events;
        _ownProcessId = ownProcessId;
        _logger = logger ?? NullAppLogger.Instance;
        _guard = guard ?? new WindowActionGuard();
    }

    /// <summary>"Pause Rules" in the tray menu (spec section 32).</summary>
    public bool Paused { get; set; }

    /// <summary>
    /// Raised when a rule has just sent a window to a desktop other than the one being looked at.
    /// The interface uses it to offer going there; the rule itself never switches on its own.
    /// </summary>
    public event EventHandler<WindowMovedEventArgs>? WindowMovedToAnotherDesktop;

    /// <summary>
    /// True while the rules are being applied to the windows that were already open, which
    /// happens at start up. Announcing every one of those moves would greet the user with a pile
    /// of questions about windows that were already where they are.
    /// </summary>
    /// <summary>Ventanas que ya hemos visto alguna vez, para saber cual acaba de nacer.</summary>
    private readonly HashSet<nint> _seen = new();

    private bool _applyingToExistingWindows;

    /// <summary>
    /// Apunta las ventanas que ya hay y empieza a escuchar los eventos de ventana. Llamarlo dos
    /// veces no hace nada.
    /// </summary>
    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        // Las que ya estaban se apuntan antes de escuchar nada, y da igual que las reglas se
        // apliquen o no al arrancar: sin esto, la primera vez que una de ellas asomara pasaria por
        // recien nacida, y a una aplicacion con "ir a la ventana que ya tienes" se le cerraria una
        // ventana que llevaba abierta toda la mañana.
        Remember();

        _events.WindowCreated += OnWindowEvent;
        _events.WindowShown += OnWindowEvent;
        _events.ForegroundWindowChanged += OnWindowEvent;
        _events.WindowDestroyed += OnWindowDestroyed;
        _subscribed = true;
    }

    private void Remember()
    {
        try
        {
            foreach (WindowInfo window in _windows.GetTopLevelWindows())
            {
                _seen.Add(window.Handle);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.RuleFailed, ex, ("Where", nameof(Remember)));
        }
    }

    /// <summary>Deja de escuchar y olvida las ventanas vistas y el freno contra bucles.</summary>
    public void Stop()
    {
        if (!_subscribed)
        {
            return;
        }

        _events.WindowCreated -= OnWindowEvent;
        _events.WindowShown -= OnWindowEvent;
        _events.ForegroundWindowChanged -= OnWindowEvent;
        _events.WindowDestroyed -= OnWindowDestroyed;
        _subscribed = false;
        _guard.Clear();
        _seen.Clear();
    }

    /// <summary>Applies the rules to every window that is already open, e.g. right after startup.</summary>
    public void ApplyToExistingWindows()
    {
        _applyingToExistingWindows = true;

        try
        {
            foreach (WindowInfo window in _windows.GetTopLevelWindows())
            {
                _seen.Add(window.Handle);
                Apply(window, isNewWindow: false);
            }
        }
        finally
        {
            _applyingToExistingWindows = false;
        }
    }

    private void OnWindowDestroyed(object? sender, WindowDetectedEventArgs e)
    {
        _guard.Forget(e.Handle);

        // Windows reaprovecha los identificadores de ventana. Si no se olvidara el de una ventana
        // cerrada, la siguiente que naciera con ese mismo numero pasaria por conocida.
        _seen.Remove(e.Handle);
    }

    private void OnWindowEvent(object? sender, WindowDetectedEventArgs e)
    {
        if (e.Window is null)
        {
            return;
        }

        // Recien nacida es la que no habiamos visto nunca, y no la que llega con el evento de
        // creacion. Se midio: EVENT_OBJECT_CREATE salta cuando la ventana aun no tiene titulo ni
        // es visible, asi que el filtro de ventanas manejables lo descarta y la ventana solo llega
        // mas tarde, ya como "mostrada". Atado a la creacion, "ir a la ventana que ya tienes" no
        // se ejecutaba practicamente nunca con ventanas de verdad.
        //
        // Se apunta siempre, tambien cuando el aviso es un simple cambio de primer plano, para que
        // la de delante quede registrada aunque nunca la hayamos visto nacer.
        bool primeraVez = _seen.Add(e.Window.Handle);

        // Y el primer plano no la hace nueva por mucho que sea la primera vez que la vemos: ahi
        // esta el motivo original de atarse a la creacion, que era no cerrarle una ventana al
        // usuario cada vez que alterna con Alt+Tab.
        bool nueva = primeraVez && e.Kind is WindowEventKind.Created or WindowEventKind.Shown;

        Apply(e.Window, isNewWindow: nueva);
    }

    private void Apply(WindowInfo window, bool isNewWindow)
    {
        if (Paused || _disposed)
        {
            return;
        }

        // Never react to our own windows: that is the shortest possible feedback loop.
        if (window.ProcessId == _ownProcessId)
        {
            return;
        }

        RuleEvaluationResult evaluation;

        try
        {
            evaluation = _engine.Evaluate(window);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.RuleFailed, ex, ("Window", window.Handle), ("Process", window.ProcessName));
            return;
        }

        if (!evaluation.HasActions)
        {
            return;
        }

        // Before anything else is done to this window, because if it is a duplicate it is about
        // to be asked to close and everything else would be work done to a window on its way out.
        // Preguntar es el comportamiento de serie, y no reutilizar ni abrir de nuevo. Pinchar una
        // aplicacion que ya esta abierta en otro escritorio no dice por si solo si se quiere ir
        // alli o quedarse aqui, y las dos respuestas son razonables segun el momento. La regla
        // sirve para no volver a preguntar: "ir" se va sin mas, "abrir nueva instancia" se queda.
        if (isNewWindow
            && !_applyingToExistingWindows
            && evaluation.Actions.NewInstance is not true
            && TryReuseExistingWindow(window, evaluation))
        {
            return;
        }

        string signature = evaluation.Actions.ComputeSignature();

        if (!_guard.TryBegin(window.Handle, signature))
        {
            _logger.Trace(
                LogEvents.RuleSkipped,
                ("Window", window.Handle),
                ("Process", window.ProcessName),
                ("Reason", "debounced"));
            return;
        }

        try
        {
            _logger.Debug(
                LogEvents.RuleMatched,
                ("Window", window.Handle),
                ("Process", window.ProcessName),
                ("Rules", string.Join(", ", evaluation.MatchedRules.Select(r => r.Name))));

            ApplyActions(window, evaluation);
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.RuleFailed, ex, ("Window", window.Handle), ("Process", window.ProcessName));
        }
        finally
        {
            _guard.End(window.Handle);
        }
    }

    /// <summary>
    /// Sends the user to the window this application already had and asks the one that has just
    /// opened to go away.
    /// </summary>
    /// <remarks>
    /// This is the answer to clicking an application's icon and being given a second empty window
    /// on the desktop you are on, when what you meant was "show me the one I already have".
    /// Whether an application reuses its window is the application's own decision, and most of
    /// the stubborn ones do not, so it is undone afterwards rather than prevented: by the time
    /// any of this can be noticed, the window exists.
    ///
    /// The new window is asked to close rather than made to. An application with something
    /// unsaved in it gets to say so.
    /// </remarks>
    /// <returns>True when the duplicate was dealt with and nothing else should be done to it.</returns>
    private bool TryReuseExistingWindow(WindowInfo duplicate, RuleEvaluationResult evaluation)
    {
        WindowInfo? existing = FindExistingWindow(duplicate);

        if (existing is null)
        {
            // Nothing to go back to: this is the first window, and it is perfectly welcome.
            return false;
        }

        _logger.Trace(
            LogEvents.RuleSkipped,
            ("Action", "ReuseExistingWindow"),
            ("Process", duplicate.ProcessName),
            ("AlreadyOpenAs", existing.HandleText));

        Guid? desktop = _desktops.GetWindowDesktopId(existing.Handle);

        if (desktop is not Guid target)
        {
            _logger.Info(
                LogEvents.RuleSkipped,
                ("Action", "ReuseExistingWindow"),
                ("Process", duplicate.ProcessName),
                ("Why", "ExistingWindowHasNoDesktop"));

            // La que ya estaba no vive en ningún escritorio concreto: está fijada en todos, o el
            // backend no sabe decirlo. No hay adónde mandar a la nueva.
            return false;
        }

        VirtualDesktopInfo? current = _desktops.GetCurrentDesktop();
        VirtualDesktopInfo? info = _desktops.GetDesktops().FirstOrDefault(d => d.Id == target);

        // "Ir a la ventana que la aplicación ya tiene" significa lo mismo esté donde esté esa
        // ventana: acabar con una sola. Antes no: si la recién abierta nacía en otro escritorio se
        // la mandaba junto a la otra y ahí se quedaba, y el usuario terminaba con las dos. Se
        // midió lanzando Outlook desde otro escritorio: dos ventanas antes, tres después.
        //
        // Se le pide que se cierre, no se la mata: si lleva algo sin guardar, que lo diga.
        if (evaluation.Actions.ReuseExistingWindow is true)
        {
            OperationResult went = current is not null && current.Id == target
                ? OperationResult.Ok()
                : _desktops.SwitchToDesktop(target);

            _windows.ActivateWindow(existing.Handle);

            OperationResult closed = _windows.CloseWindow(duplicate.Handle);

            _logger.Info(
                LogEvents.RuleExecuted,
                ("Rule", RuleBehind(evaluation, nameof(ResolvedWindowActions.ReuseExistingWindow))),
                ("Action", "GoToExistingWindow"),
                ("Process", duplicate.ProcessName),
                ("Desktop", info?.Number),
                ("KeptOpen", existing.HandleText),
                ("Closed", duplicate.HandleText),
                ("Switched", went.Status),
                ("Result", closed.Status));

            return true;
        }

        // Sin "ir" en la regla y ya estando donde vive la aplicación —casi siempre porque Windows
        // nos ha traído al activar su icono—, no hay nada que mover ni adónde ofrecer ir. Y la
        // única decisión que quedaría es cerrar una ventana que el usuario acaba de abrir, que no
        // se hace sin que lo pida.
        if (current is not null && current.Id == target)
        {
            _logger.Info(
                LogEvents.RuleSkipped,
                ("Rule", RuleBehind(evaluation, nameof(ResolvedWindowActions.Desktop))),
                ("Action", "ReuseExistingWindow"),
                ("Process", duplicate.ProcessName),
                ("Why", "AlreadyOnThatDesktop"));

            return false;
        }

        OperationResult moved = _desktops.MoveWindowToDesktop(duplicate.Handle, target);

        _logger.Info(
            LogEvents.RuleExecuted,
            ("Rule", RuleBehind(evaluation, nameof(ResolvedWindowActions.ReuseExistingWindow))),
            ("Action", "ReuseExistingWindow"),
            ("Process", duplicate.ProcessName),
            ("AlreadyOpenAs", existing.HandleText),
            ("Moved", duplicate.HandleText),
            ("Result", moved.Status));

        if (!moved.IsSuccess)
        {
            return false;
        }

        // Y si no, se pregunta en lugar de arrastrar al usuario. Esto antes cambiaba de escritorio
        // sin avisar y cerraba la ventana recién abierta, lo cual se llevaba por delante la página
        // o la notificación que se acababa de pinchar. Ahora la recién llegada se va a donde vive
        // su aplicación, en segundo plano, y quien decide si ir es el usuario.
        if (info is not null)
        {
            AnnounceMove(duplicate, info);
        }

        return true;
    }

    /// <summary>
    /// Another window of the same application, or null when this is the only one.
    /// </summary>
    /// <remarks>
    /// Matched on the executable and not on the process id: an application may well answer a
    /// second launch by starting a whole new process, and to the user it is still "the same
    /// program". Minimised windows count, since going to a minimised window is exactly what
    /// clicking the icon is usually meant to do.
    /// </remarks>
    private WindowInfo? FindExistingWindow(WindowInfo duplicate)
    {
        if (duplicate.ProcessName.Length == 0)
        {
            return null;
        }

        WindowInfo? best = null;

        foreach (WindowInfo candidate in _windows.GetTopLevelWindows())
        {
            if (candidate.Handle == duplicate.Handle
                || candidate.ProcessId == _ownProcessId
                || !string.Equals(candidate.ProcessName, duplicate.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A window that is not minimised is the better destination, so it wins over one that
            // is; otherwise the first one found will do.
            if (best is null || (best.IsMinimized && !candidate.IsMinimized))
            {
                best = candidate;
            }
        }

        return best;
    }

    private void ApplyActions(WindowInfo window, RuleEvaluationResult evaluation)
    {
        ResolvedWindowActions actions = evaluation.Actions;

        if (actions.AlwaysOnTop is bool alwaysOnTop && alwaysOnTop != window.IsTopMost)
        {
            Report("AlwaysOnTop", window, actions, _windows.SetAlwaysOnTop(window.Handle, alwaysOnTop));
        }

        if (actions.Maximized is bool maximized && maximized != window.IsMaximized)
        {
            Report(
                "Maximized",
                window,
                actions,
                _windows.SetWindowState(window.Handle, maximized ? WindowVisualState.Maximized : WindowVisualState.Normal));
        }

        if (actions.Bounds is WindowBounds bounds && bounds != window.Bounds)
        {
            Report(
                "Bounds",
                window,
                actions,
                _windows.MoveWindow(window.Handle, bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        if (actions.Monitor is int monitor)
        {
            // Sólo cuando hay a dónde mover. Con un solo monitor esto no puede hacer nada útil, y
            // antes contestaba "no soportado" en cada evaluación, llenando el log de ruido.
            if (_windows.GetMonitors().Count > 1)
            {
                Report("Monitor", window, actions, _windows.MoveWindowToMonitor(window.Handle, monitor));
            }
        }

        // Unpinning comes before the move, and pinning after it, because the two actions fight:
        // Windows silently unpins a view when it is sent to one desktop (measured on build
        // 26200: the move answers S_OK and the window stops being on all desktops). In this
        // order the rule ends in the state it asks for instead of in whichever state the last
        // side effect happened to leave.
        if (actions.PinWindow is false)
        {
            Report("PinWindow", window, actions, Pin(window, false));
        }

        if (actions.Desktop is not null && !StaysEverywhere(window, actions))
        {
            Report("Desktop", window, actions, MoveToDesktop(window, actions.Desktop));
        }

        if (actions.PinWindow is true)
        {
            Report("PinWindow", window, actions, Pin(window, true));
        }

        if (actions.PinApplication is bool pinApplication)
        {
            Report("PinApplication", window, actions, PinApp(window, pinApplication));
        }
    }

    /// <summary>
    /// Sends the window to the desktop the rule asks for.
    /// </summary>
    /// <remarks>
    /// It never switches the user to that desktop, unlike the equivalent command: a rule fires
    /// when a window appears, often in the background, and yanking the whole screen away would be
    /// hostile. That is what <c>followMovedWindow</c> is for, and it only applies to explicit
    /// commands (spec section 16).
    /// </remarks>
    private OperationResult MoveToDesktop(WindowInfo window, string desktop)
    {
        if (!_desktops.Capabilities.CanMoveWindow || !_desktops.Capabilities.CanEnumerateDesktops)
        {
            return OperationResult.NotSupported(
                $"The '{_desktops.BackendName}' backend cannot move windows between desktops.");
        }

        VirtualDesktopInfo? target = DesktopResolver.Resolve(desktop, _desktops.GetDesktops());

        if (target is null)
        {
            return OperationResult.Fail($"There is no desktop called '{desktop}'.");
        }

        // A window shown on every desktop is already on the target one, so there is nowhere to
        // move it to. Asking anyway would not move it: it would quietly cancel the "show on all
        // desktops" the user asked for, and it would do it again on every focus change, because
        // the rules also run on ForegroundWindowChanged. A rule that really wants the window on
        // one desktop says so by unpinning it, which ApplyActions has already done by this point.
        if (_desktops.IsWindowPinned(window.Handle) == true)
        {
            return OperationResult.Ok();
        }

        // Already there: doing it again would only produce more events.
        if (_desktops.GetWindowDesktopId(window.Handle) == target.Id)
        {
            return OperationResult.Ok();
        }

        OperationResult result = _desktops.MoveWindowToDesktop(window.Handle, target.Id);

        if (result.IsSuccess)
        {
            AnnounceMove(window, target);
        }

        return result;
    }

    /// <summary>
    /// Tells whoever is listening that a window has just landed somewhere the user is not
    /// looking, so it can be offered to go there.
    /// </summary>
    /// <summary>El nombre de la regla que decidio algo, para el diario.</summary>
    private static string RuleBehind(RuleEvaluationResult evaluation, string property) =>
        evaluation.Actions.Sources.TryGetValue(property, out string? rule) ? rule : "?";

    private void AnnounceMove(WindowInfo window, VirtualDesktopInfo target)
    {
        if (_applyingToExistingWindows || WindowMovedToAnotherDesktop is null)
        {
            return;
        }

        // If it landed on the desktop being looked at, there is nothing to offer.
        if (_desktops.GetCurrentDesktop()?.Id == target.Id)
        {
            return;
        }

        try
        {
            WindowMovedToAnotherDesktop.Invoke(this, new WindowMovedEventArgs(window, target));
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.RuleFailed, ex, ("Where", nameof(AnnounceMove)));
        }
    }

    /// <summary>
    /// Si la ventana ya se ve en todos los escritorios y la regla la va a dejar así.
    /// </summary>
    /// <remarks>
    /// Entonces no se la mueve, porque no se puede: Windows responde <c>0x8007139F</c>, estado no
    /// válido, al mandar a un escritorio algo que está anclado. Una regla con escritorio y
    /// anclaje a la vez —que es una combinación normal— dejaba un aviso de fallo en el diario
    /// cada vez que se reaplicaba, avisando de algo que ya estaba como se pedía.
    ///
    /// La primera vez sí se mueve, cuando todavía no está anclada, así que conserva el escritorio
    /// de origen por si algún día se la desancla. Lo que se evita es pelearse a partir de la
    /// segunda.
    /// </remarks>
    private bool StaysEverywhere(WindowInfo window, ResolvedWindowActions actions)
    {
        if (actions.PinWindow is true && _desktops.IsWindowPinned(window.Handle) == true)
        {
            return true;
        }

        return actions.PinApplication is true && _desktops.IsApplicationPinned(window.Handle) == true;
    }

    private OperationResult Pin(WindowInfo window, bool pinned)
    {
        if (!_desktops.Capabilities.CanPinWindow)
        {
            return OperationResult.NotSupported($"The '{_desktops.BackendName}' backend cannot pin windows.");
        }

        if (_desktops.IsWindowPinned(window.Handle) == pinned)
        {
            return OperationResult.Ok();
        }

        return _desktops.SetWindowPinned(window.Handle, pinned);
    }

    /// <summary>Anclar la aplicacion entera, y no solo esta ventana suya.</summary>
    /// <remarks>
    /// La diferencia es la que se pedia: anclar ventanas alcanza solo a las que existen en ese
    /// momento, mientras que Windows se acuerda de una aplicacion anclada y se lo aplica a las que
    /// abra despues. Una videollamada de Teams es una ventana nueva.
    /// </remarks>
    private OperationResult PinApp(WindowInfo window, bool pinned)
    {
        if (!_desktops.Capabilities.CanPinApplication)
        {
            return OperationResult.NotSupported(
                $"The '{_desktops.BackendName}' backend cannot pin whole applications.");
        }

        if (_desktops.IsApplicationPinned(window.Handle) == pinned)
        {
            return OperationResult.Ok();
        }

        return _desktops.SetApplicationPinned(window.Handle, pinned);
    }

    private void Report(string action, WindowInfo window, ResolvedWindowActions actions, OperationResult result)
    {
        actions.Sources.TryGetValue(action, out string? rule);

        if (result.IsSuccess)
        {
            _logger.Info(
                LogEvents.RuleExecuted,
                ("Rule", rule ?? "?"),
                ("Action", action),
                ("Window", window.Handle),
                ("Process", window.ProcessName));
            return;
        }

        // Neither of these is an error worth a warning: Unsupported is a normal answer from a
        // backend that cannot do something (spec section 14), and InvalidWindow is what the
        // helper windows many applications create answer, since they are not real windows to act
        // on. Warning about them on every match would just fill the log (spec section 34).
        LogLevel level = result.Status is OperationStatus.Unsupported or OperationStatus.InvalidWindow
            ? LogLevel.Debug
            : LogLevel.Warning;

        _logger.Log(
            level,
            LogEvents.RuleFailed,
            result.Error,
            null,
            ("Rule", rule ?? "?"),
            ("Action", action),
            ("Status", result.Status),
            ("Window", window.Handle),
            ("Process", window.ProcessName));
    }

    /// <summary>Para el servicio; después ya no aplica ninguna regla.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}
