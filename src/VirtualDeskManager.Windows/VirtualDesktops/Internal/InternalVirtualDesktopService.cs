using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;
using VirtualDeskManager.Windows.Navigation;

namespace VirtualDeskManager.Windows.VirtualDesktops.Internal;

/// <summary>
/// Advanced backend built on the shell interfaces Explorer and Task View use (spec sections 13
/// and 43). It is what makes moving a window of another application to a different desktop, and
/// showing a window on every desktop, actually possible: the documented API can only move windows
/// that belong to the calling process and cannot pin at all.
/// </summary>
/// <remarks>
/// These interfaces are undocumented and Microsoft changes their identifiers between builds, so
/// nothing here is taken on faith:
/// <list type="number">
///   <item>the interface identifier is discovered by asking the shell about each known candidate;</item>
///   <item>the order of the methods is then checked against the registry, which independently
///   knows how many desktops exist and which one is active;</item>
///   <item>only if both agree does the backend declare itself usable.</item>
/// </list>
/// Any failure means falling back to the public backend with reduced capabilities, never a crash
/// and never a wrong action (spec section 44).
/// </remarks>
public sealed class InternalVirtualDesktopService : IVirtualDesktopService, IDisposable
{
    private readonly object _gate = new();

    /// <summary>Quien sabe a que aplicacion pertenece una ventana, que casi nunca lo declara.</summary>
    private readonly VirtualDeskManager.Windows.Taskbar.AppIdResolver _appIds;
    private readonly IAppLogger _logger;
    private readonly IVirtualDesktopManagerInternal _manager;
    private readonly IApplicationViewCollection _views;
    private readonly IVirtualDesktopPinnedApps? _pinnedApps;
    private readonly IVirtualDesktopService? _documented;
    private readonly Guid _desktopIid;

    private bool _disposed;

    private InternalVirtualDesktopService(
        IVirtualDesktopManagerInternal manager,
        IApplicationViewCollection views,
        IVirtualDesktopPinnedApps? pinnedApps,
        IVirtualDesktopService? documented,
        Guid desktopIid,
        IAppLogger logger)
    {
        _manager = manager;
        _views = views;
        _pinnedApps = pinnedApps;
        _documented = documented;
        _desktopIid = desktopIid;
        _logger = logger;
        _appIds = new VirtualDeskManager.Windows.Taskbar.AppIdResolver(logger);

        Capabilities = new VirtualDesktopCapabilities
        {
            CanGetWindowDesktop = true,
            CanMoveWindow = true,
            CanEnumerateDesktops = true,
            CanSwitchDesktop = true,
            // Creating goes through the documented Ctrl+Win+D shortcut, not through the internal
            // table: the slot for it lives in the stretch whose order changes between builds.
            CanCreateDesktop = true,
            // Eliminar, igual que crear: por el atajo documentado (Ctrl+Win+F4) y comprobado después.
            CanRemoveDesktop = true,
            CanRenameDesktop = true,
            CanPinWindow = pinnedApps is not null,

            // Ya no es false: se apoya en el resolutor de AppUserModelID, que es lo que faltaba.
            // Windows ancla aplicaciones por ese identificador, y la ventana casi nunca lo dice.
            CanPinApplication = pinnedApps is not null
        };
    }

    /// <summary>Always "Windows internal shell API".</summary>
    public string BackendName => "Windows internal shell API";

    /// <summary>
    /// Everything. Pinning windows and applications depends on the build
    /// exposing the pinned apps interface.
    /// </summary>
    public VirtualDesktopCapabilities Capabilities { get; }

    // ------------------------------------------------------------------ creation

    /// <param name="documented">
    /// The public backend. Reading the desktop of a window is documented and works for any
    /// window, so there is no reason to use an undocumented interface for it.
    /// </param>
    /// <param name="logger">Where to record which interface was found, or why none was. Null logs nothing.</param>
    public static InternalVirtualDesktopService? TryCreate(
        IVirtualDesktopService? documented = null,
        IAppLogger? logger = null)
    {
        IAppLogger log = logger ?? NullAppLogger.Instance;

        try
        {
            Type? shellType = Type.GetTypeFromCLSID(InternalClsids.ImmersiveShell);

            if (shellType is null || Activator.CreateInstance(shellType) is not IShellServiceProvider provider)
            {
                log.Warn(LogEvents.DesktopBackendUnavailable, "The shell service provider is not available.");
                return null;
            }

            IVirtualDesktopManagerInternal? manager = null;

            foreach (Guid candidate in InternalInterfaceIds.ManagerCandidates)
            {
                if (TryQuery(provider, InternalClsids.VirtualDesktopManagerInternal, candidate) is IVirtualDesktopManagerInternal found)
                {
                    manager = found;
                    log.Info(LogEvents.DesktopBackendSelected, ("Interface", "IVirtualDesktopManagerInternal"), ("Iid", candidate));
                    break;
                }
            }

            if (manager is null)
            {
                log.Warn(
                    LogEvents.DesktopBackendUnavailable,
                    "This Windows build does not answer to any known identifier of the internal desktop interface.",
                    ("WindowsVersion", Environment.OSVersion.Version));
                return null;
            }

            if (TryQuery(provider, InternalClsids.ApplicationViewCollection, InternalInterfaceIds.ApplicationViewCollection)
                is not IApplicationViewCollection views)
            {
                log.Warn(LogEvents.DesktopBackendUnavailable, "The application view collection is not available.");
                return null;
            }

            // Pinning is optional: without it the backend still moves windows between desktops.
            IVirtualDesktopPinnedApps? pinned =
                TryQuery(provider, InternalClsids.VirtualDesktopPinnedApps, InternalInterfaceIds.VirtualDesktopPinnedApps)
                    as IVirtualDesktopPinnedApps;

            Guid desktopIid = InternalInterfaceIds.DesktopCandidates[0];
            InternalVirtualDesktopService service = new(manager, views, pinned, documented, desktopIid, log);

            if (!service.LooksTrustworthy(log))
            {
                return null;
            }

            return service;
        }
        catch (Exception ex)
        {
            log.Error(LogEvents.DesktopBackendUnavailable, ex);
            return null;
        }
    }

    private static object? TryQuery(IShellServiceProvider provider, Guid service, Guid iid)
    {
        Guid serviceId = service;
        Guid interfaceId = iid;
        nint raw = 0;

        try
        {
            if (provider.QueryService(ref serviceId, ref interfaceId, out raw) != NativeConstants.S_OK || raw == 0)
            {
                return null;
            }

            return Marshal.GetObjectForIUnknown(raw);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (raw != 0)
            {
                Marshal.Release(raw);
            }
        }
    }

    /// <summary>
    /// Checks the answers of the interface against the registry before trusting it. If a Windows
    /// update reshuffles the methods, the numbers stop matching and the backend steps aside
    /// instead of doing something unpredictable.
    /// </summary>
    private bool LooksTrustworthy(IAppLogger log)
    {
        IReadOnlyList<Guid> expected = DesktopNameRegistry.ReadDesktopIds();

        if (expected.Count == 0)
        {
            // Nothing to compare against: accept, but say so.
            log.Warn(LogEvents.DesktopBackendSelected, "The desktop list could not be read from the registry; the backend was not cross-checked.");
            return true;
        }

        try
        {
            if (_manager.GetCount(out int count) != NativeConstants.S_OK || count != expected.Count)
            {
                log.Warn(
                    LogEvents.DesktopBackendUnavailable,
                    "The internal interface does not agree with the registry on how many desktops there are.",
                    ("Interface", count),
                    ("Registry", expected.Count));
                return false;
            }

            Guid? current = DesktopNameRegistry.ReadCurrentDesktopId();
            Guid reported = GetCurrentDesktopId();

            if (current is Guid expectedCurrent && reported != expectedCurrent)
            {
                log.Warn(
                    LogEvents.DesktopBackendUnavailable,
                    "The internal interface does not agree with the registry on which desktop is active.",
                    ("Interface", reported),
                    ("Registry", expectedCurrent));
                return false;
            }

            log.Info(
                LogEvents.DesktopBackendSelected,
                ("Backend", BackendName),
                ("Desktops", count),
                ("Verified", true));

            return true;
        }
        catch (Exception ex)
        {
            log.Error(LogEvents.DesktopBackendUnavailable, ex, ("Where", nameof(LooksTrustworthy)));
            return false;
        }
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Desktops in the order Task View shows them, with the names stored in the registry.</summary>
    /// <remarks>Empty when the shell does not answer.</remarks>
    public IReadOnlyList<VirtualDesktopInfo> GetDesktops()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Array.Empty<VirtualDesktopInfo>();
            }

            try
            {
                if (_manager.GetDesktops(out IObjectArray? array) != NativeConstants.S_OK || array is null)
                {
                    return Array.Empty<VirtualDesktopInfo>();
                }

                array.GetCount(out uint count);
                List<VirtualDesktopInfo> desktops = new((int)count);

                for (uint index = 0; index < count; index++)
                {
                    Guid iid = _desktopIid;

                    if (array.GetAt(index, ref iid, out object item) != NativeConstants.S_OK)
                    {
                        continue;
                    }

                    if (item is IVirtualDesktop desktop && desktop.GetId(out Guid id) == NativeConstants.S_OK)
                    {
                        desktops.Add(new VirtualDesktopInfo(id, (int)index, DesktopNameRegistry.ReadName(id)));
                    }

                    Release(item);
                }

                return desktops;
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(GetDesktops)));
                return Array.Empty<VirtualDesktopInfo>();
            }
        }
    }

    /// <summary>The desktop on screen, or null when the shell does not say.</summary>
    public VirtualDesktopInfo? GetCurrentDesktop()
    {
        Guid current = GetCurrentDesktopId();
        return current == Guid.Empty ? null : GetDesktops().FirstOrDefault(d => d.Id == current);
    }

    /// <summary>
    /// Desktop the window lives on, read through the documented backend. Null for a pinned window,
    /// which lives on all of them, or when it cannot be read.
    /// </summary>
    public Guid? GetWindowDesktopId(nint hwnd)
    {
        lock (_gate)
        {
            if (_disposed || hwnd == 0)
            {
                return null;
            }

            try
            {
                Guid? desktop = _documented?.GetWindowDesktopId(hwnd);

                if (desktop is null)
                {
                    return null;
                }

                // A pinned window answers with a GUID that belongs to no desktop at all
                // (measured: c2ddea68-... on build 26200), because it is on every one of them.
                // Callers look that up in the desktop list, find nothing, and conclude the window
                // is nowhere: left and right moves lose their origin and "is it on this desktop?"
                // answers no for a window that is on all of them. Null is both true and already
                // handled everywhere, so it is the honest answer.
                //
                // Re-entering _gate here is deliberate and safe: the lock is per thread.
                return IsWindowPinned(hwnd) == true ? null : desktop;
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(GetWindowDesktopId)));
                return null;
            }
        }
    }

    /// <summary>True when the window is on the desktop on screen, is pinned, or its desktop is unknown.</summary>
    public bool IsWindowOnCurrentDesktop(nint hwnd)
    {
        Guid? windowDesktop = GetWindowDesktopId(hwnd);
        return windowDesktop is null || windowDesktop == GetCurrentDesktopId();
    }

    /// <summary>Whether this one window is shown on every desktop, or null when the shell cannot tell.</summary>
    public bool? IsWindowPinned(nint hwnd)
    {
        lock (_gate)
        {
            if (_disposed || _pinnedApps is null)
            {
                return null;
            }

            try
            {
                object? view = GetView(hwnd);

                if (view is null)
                {
                    return null;
                }

                try
                {
                    return _pinnedApps.IsViewPinned(view, out bool pinned) == NativeConstants.S_OK ? pinned : null;
                }
                finally
                {
                    Release(view);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(IsWindowPinned)));
                return null;
            }
        }
    }

    // ------------------------------------------------------------------ operations

    /// <summary>Moves a window of any application to another desktop, through its application view.</summary>
    public OperationResult MoveWindowToDesktop(nint hwnd, Guid desktopId)
    {
        if (hwnd == 0 || !User32.IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        if (desktopId == Guid.Empty)
        {
            return OperationResult.Fail("The target desktop identifier is empty.");
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return OperationResult.NotSupported();
            }

            object? view = null;
            object? desktop = null;

            try
            {
                view = GetView(hwnd);

                if (view is null)
                {
                    return OperationResult.InvalidWindow("Windows does not expose an application view for that window.");
                }

                desktop = FindDesktop(desktopId);

                if (desktop is null)
                {
                    return OperationResult.Fail("That desktop does not exist any more.");
                }

                int hr = _manager.MoveViewToDesktop(view, desktop);

                if (hr != NativeConstants.S_OK)
                {
                    _logger.Warn(
                        LogEvents.DesktopOperationFailed,
                        ("Operation", nameof(MoveWindowToDesktop)),
                        ("Window", hwnd),
                        ("HRESULT", "0x" + hr.ToString("X8")));

                    return hr == NativeConstants.E_ACCESSDENIED
                        ? OperationResult.Denied()
                        : OperationResult.Fail($"The window could not be moved (HRESULT 0x{hr:X8}).");
                }

                _logger.Info(
                    LogEvents.DesktopOperation,
                    ("Operation", nameof(MoveWindowToDesktop)),
                    ("Window", hwnd),
                    ("Desktop", desktopId));

                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(MoveWindowToDesktop)));
                return OperationResult.Fail(ex.Message);
            }
            finally
            {
                Release(view);
                Release(desktop);
            }
        }
    }

    /// <summary>
    /// Adds a desktop with the shell's own Ctrl+Win+D and confirms it against the registry.
    /// </summary>
    /// <remarks>
    /// Synthetic input is fire and forget: it says the keys were delivered, never that the shell
    /// acted on them. So the result is not trusted, it is checked. The registry is read until a
    /// new identifier appears or the wait runs out, which also absorbs the shell taking its time.
    /// </remarks>
    public VirtualDesktopInfo? CreateDesktop()
    {
        IReadOnlyList<Guid> before = DesktopNameRegistry.ReadDesktopIds();

        OperationResult sent = ShellShortcut.Send(NativeConstants.VK_D);

        if (!sent.IsSuccess)
        {
            _logger.Warn(
                LogEvents.DesktopOperationFailed,
                ("Operation", nameof(CreateDesktop)),
                ("Reason", sent.Error ?? "the shortcut could not be sent"));

            return null;
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(100);

            IReadOnlyList<Guid> after = DesktopNameRegistry.ReadDesktopIds();

            if (after.Count <= before.Count)
            {
                continue;
            }

            Guid created = after.Except(before).FirstOrDefault();

            if (created == Guid.Empty)
            {
                continue;
            }

            _logger.Info(LogEvents.DesktopOperation, ("Operation", nameof(CreateDesktop)), ("Desktop", created));

            return new VirtualDesktopInfo(created, after.Count - 1, DesktopNameRegistry.ReadName(created));
        }

        _logger.Warn(
            LogEvents.DesktopOperationFailed,
            ("Operation", nameof(CreateDesktop)),
            ("Reason", "the shell did not create a desktop within two seconds"));

        return null;
    }

    /// <summary>
    /// Elimina un escritorio con el atajo documentado Ctrl+Win+F4, que cierra el que se está viendo.
    /// </summary>
    /// <remarks>
    /// No por la tabla interna, por lo mismo que crear: su ranura está en el tramo cuyo orden cambia
    /// entre compilaciones, y acertar la equivocada se lleva el Explorador por delante.
    ///
    /// Como el atajo cierra el escritorio que está delante, primero se va a él, y **sólo si se
    /// comprueba que de verdad está delante** se pulsa: si el cambio no hubiera llegado, se cerraría
    /// otro. Después se confirma en el registro que ha desaparecido, y se vuelve a donde se estaba.
    /// Las ventanas no se cierran: Windows las pasa al escritorio de al lado.
    /// </remarks>
    public OperationResult RemoveDesktop(Guid desktopId)
    {
        IReadOnlyList<Guid> before = DesktopNameRegistry.ReadDesktopIds();

        if (!before.Contains(desktopId))
        {
            return OperationResult.Fail("That desktop does not exist any more.");
        }

        if (before.Count <= 1)
        {
            return OperationResult.Fail("Windows needs at least one desktop.");
        }

        Guid? backTo = GetCurrentDesktop()?.Id;

        if (backTo != desktopId)
        {
            OperationResult switched = SwitchToDesktop(desktopId);

            if (!switched.IsSuccess)
            {
                return switched;
            }
        }

        if (!WaitUntil(() => GetCurrentDesktop()?.Id == desktopId))
        {
            _logger.Warn(
                LogEvents.DesktopOperationFailed,
                ("Operation", nameof(RemoveDesktop)),
                ("Reason", "the view did not reach the desktop to remove, so nothing was closed"));

            return OperationResult.Fail("Windows did not show that desktop, so it was not removed.");
        }

        OperationResult sent = ShellShortcut.Send(NativeConstants.VK_F4);

        if (!sent.IsSuccess)
        {
            ReturnTo(backTo, desktopId);
            return sent;
        }

        bool gone = WaitUntil(() => !DesktopNameRegistry.ReadDesktopIds().Contains(desktopId));

        ReturnTo(backTo, desktopId);

        if (!gone)
        {
            _logger.Warn(
                LogEvents.DesktopOperationFailed,
                ("Operation", nameof(RemoveDesktop)),
                ("Reason", "the shell did not remove the desktop within two seconds"));

            return OperationResult.Fail("Windows did not remove the desktop.");
        }

        _logger.Info(LogEvents.DesktopOperation, ("Operation", nameof(RemoveDesktop)), ("Desktop", desktopId));
        return OperationResult.Ok();
    }

    /// <summary>Vuelve al escritorio en el que se estaba, si no era el que se ha eliminado.</summary>
    private void ReturnTo(Guid? desktop, Guid removed)
    {
        if (desktop is Guid target && target != removed && GetCurrentDesktop()?.Id != target)
        {
            SwitchToDesktop(target);
        }
    }

    /// <summary>Espera hasta dos segundos a que se cumpla algo que el shell hace a su ritmo.</summary>
    private static bool WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return condition();
    }

    /// <summary>Renames a desktop by writing its name in the registry, where the shell keeps it.</summary>
    public OperationResult SetDesktopName(Guid desktopId, string name)
    {
        if (desktopId == Guid.Empty)
        {
            return OperationResult.Fail("The desktop identifier is empty.");
        }

        if (!DesktopNameRegistry.ReadDesktopIds().Contains(desktopId))
        {
            return OperationResult.Fail("That desktop does not exist any more.");
        }

        if (!DesktopNameRegistry.WriteName(desktopId, name))
        {
            return OperationResult.Fail("The name could not be written.");
        }

        _logger.Info(LogEvents.DesktopOperation, ("Operation", nameof(SetDesktopName)), ("Desktop", desktopId));
        return OperationResult.Ok();
    }

    /// <summary>Puts another desktop on screen, the same as choosing it in Task View.</summary>
    public OperationResult SwitchToDesktop(Guid desktopId)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return OperationResult.NotSupported();
            }

            object? desktop = null;

            try
            {
                desktop = FindDesktop(desktopId);

                if (desktop is null)
                {
                    return OperationResult.Fail("That desktop does not exist any more.");
                }

                int hr = _manager.SwitchDesktop(desktop);

                if (hr != NativeConstants.S_OK)
                {
                    return OperationResult.Fail($"The desktop could not be switched (HRESULT 0x{hr:X8}).");
                }

                _logger.Info(LogEvents.DesktopOperation, ("Operation", nameof(SwitchToDesktop)), ("Desktop", desktopId));
                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(SwitchToDesktop)));
                return OperationResult.Fail(ex.Message);
            }
            finally
            {
                Release(desktop);
            }
        }
    }

    /// <summary>Shows or stops showing one window on every desktop.</summary>
    /// <remarks>Only reaches that window. To cover the windows an application opens later, see <see cref="SetApplicationPinned"/>.</remarks>
    public OperationResult SetWindowPinned(nint hwnd, bool pinned)
    {
        if (hwnd == 0 || !User32.IsWindow(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        lock (_gate)
        {
            if (_disposed || _pinnedApps is null)
            {
                return OperationResult.NotSupported("This Windows build does not expose window pinning.");
            }

            object? view = null;

            try
            {
                view = GetView(hwnd);

                if (view is null)
                {
                    return OperationResult.InvalidWindow("Windows does not expose an application view for that window.");
                }

                int hr = pinned ? _pinnedApps.PinView(view) : _pinnedApps.UnpinView(view);

                if (hr != NativeConstants.S_OK)
                {
                    return OperationResult.Fail($"The window could not be pinned (HRESULT 0x{hr:X8}).");
                }

                _logger.Info(
                    LogEvents.DesktopOperation,
                    ("Operation", nameof(SetWindowPinned)),
                    ("Window", hwnd),
                    ("Pinned", pinned));

                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(SetWindowPinned)));
                return OperationResult.Fail(ex.Message);
            }
            finally
            {
                Release(view);
            }
        }
    }

    /// <summary>
    /// Anclar la aplicación entera, y no cada una de sus ventanas.
    /// </summary>
    /// <remarks>
    /// La diferencia es justo lo que se pedía: anclar ventanas sólo alcanza a las que existen en
    /// ese momento, mientras que Windows se <b>acuerda</b> de una aplicación anclada y se lo
    /// aplica a las que abra después. Una videollamada de Teams es una ventana nueva, y con el
    /// anclaje por ventana nacía sin heredar nada.
    ///
    /// Windows identifica la aplicación por su <c>AppUserModelID</c>, no por el proceso, y por eso
    /// esto no se pudo hacer hasta tener el resolutor: la ventana casi nunca lo declara.
    /// </remarks>
    public OperationResult SetApplicationPinned(nint hwnd, bool pinned)
    {
        lock (_gate)
        {
            if (_disposed || _pinnedApps is null)
            {
                return OperationResult.NotSupported("This Windows build does not expose application pinning.");
            }

            string? appId = ApplicationIdOf(hwnd);

            if (appId is null)
            {
                return OperationResult.Fail(
                    "Windows does not say which application that window belongs to, so it cannot be pinned as one.");
            }

            try
            {
                int hr = pinned ? _pinnedApps.PinAppID(appId) : _pinnedApps.UnpinAppID(appId);

                if (hr != NativeConstants.S_OK)
                {
                    return OperationResult.Fail($"The application could not be pinned (HRESULT 0x{hr:X8}).");
                }

                _logger.Info(
                    LogEvents.DesktopOperation,
                    ("Operation", nameof(SetApplicationPinned)),
                    ("AppId", appId),
                    ("Pinned", pinned));

                return OperationResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(SetApplicationPinned)));
                return OperationResult.Fail(ex.Message);
            }
        }
    }

    /// <summary>
    /// Whether the application of the window is shown on every desktop, or null when its
    /// <c>AppUserModelID</c> cannot be found or the shell cannot tell.
    /// </summary>
    public bool? IsApplicationPinned(nint hwnd)
    {
        lock (_gate)
        {
            if (_disposed || _pinnedApps is null)
            {
                return null;
            }

            string? appId = ApplicationIdOf(hwnd);

            if (appId is null)
            {
                return null;
            }

            try
            {
                return _pinnedApps.IsAppIdPinned(appId, out bool pinned) == NativeConstants.S_OK ? pinned : null;
            }
            catch (Exception ex)
            {
                _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(IsApplicationPinned)));
                return null;
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private string? ApplicationIdOf(nint hwnd)
    {
        try
        {
            _ = User32.GetWindowThreadProcessId(hwnd, out uint processId);

            return _appIds.Resolve(hwnd, processId, Kernel32.GetProcessImagePath(processId));
        }
        catch (Exception ex)
        {
            _logger.Warn("ApplicationIdFailed", ex.Message);
            return null;
        }
    }

    private Guid GetCurrentDesktopId()
    {
        try
        {
            if (_manager.GetCurrentDesktop(out IVirtualDesktop? desktop) != NativeConstants.S_OK || desktop is null)
            {
                return Guid.Empty;
            }

            try
            {
                return desktop.GetId(out Guid id) == NativeConstants.S_OK ? id : Guid.Empty;
            }
            finally
            {
                Release(desktop);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.DesktopOperationFailed, ex, ("Operation", nameof(GetCurrentDesktopId)));
            return Guid.Empty;
        }
    }

    private object? GetView(nint hwnd) =>
        _views.GetViewForHwnd(hwnd, out object? view) == NativeConstants.S_OK ? view : null;

    private object? FindDesktop(Guid desktopId)
    {
        if (_manager.GetDesktops(out IObjectArray? array) != NativeConstants.S_OK || array is null)
        {
            return null;
        }

        array.GetCount(out uint count);

        for (uint index = 0; index < count; index++)
        {
            Guid iid = _desktopIid;

            if (array.GetAt(index, ref iid, out object item) != NativeConstants.S_OK)
            {
                continue;
            }

            if (item is IVirtualDesktop desktop
                && desktop.GetId(out Guid id) == NativeConstants.S_OK
                && id == desktopId)
            {
                return item;
            }

            Release(item);
        }

        return null;
    }

    private static void Release(object? instance)
    {
        if (instance is null || !Marshal.IsComObject(instance))
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(instance);
        }
        catch (Exception)
        {
            // Shutting down: the runtime may have released it already.
        }
    }

    /// <summary>Releases the shell COM objects. Afterwards the calls that need them answer as if the backend were unavailable.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Release(_pinnedApps);
            Release(_views);
            Release(_manager);
        }
    }
}
