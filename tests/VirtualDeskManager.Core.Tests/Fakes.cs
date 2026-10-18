using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// In memory window manager. The Win32 specific logic is isolated in the Windows project, so
/// everything above it can be tested without Windows (spec section 40).
/// </summary>
public sealed class FakeWindowManager : IWindowManager
{
    private readonly Dictionary<nint, WindowInfo> _windows = new();

    public List<(nint Handle, bool Enabled)> AlwaysOnTopCalls { get; } = new();

    public List<(nint Handle, WindowVisualState State)> StateCalls { get; } = new();

    public List<(nint Handle, int X, int Y, int Width, int Height)> MoveCalls { get; } = new();

    public OperationResult NextResult { get; set; } = OperationResult.Ok();

    public nint Foreground { get; set; }

    public void Add(WindowInfo window) => _windows[window.Handle] = window;

    public nint GetForegroundWindow() => Foreground;

    public WindowInfo? GetWindowInfo(nint hwnd) => _windows.TryGetValue(hwnd, out WindowInfo? info) ? info : null;

    public IReadOnlyList<WindowInfo> GetTopLevelWindows() => _windows.Values.ToList();

    public bool IsWindow(nint hwnd) => _windows.ContainsKey(hwnd);

    public bool IsAlwaysOnTop(nint hwnd) => GetWindowInfo(hwnd)?.IsTopMost ?? false;

    public bool IsManageableWindow(nint hwnd) => _windows.ContainsKey(hwnd);

    public uint GetWindowProcessId(nint hwnd) => GetWindowInfo(hwnd)?.ProcessId ?? 0;

    public OperationResult SetAlwaysOnTop(nint hwnd, bool enabled)
    {
        AlwaysOnTopCalls.Add((hwnd, enabled));
        return NextResult;
    }

    public OperationResult MoveWindow(nint hwnd, int x, int y, int width, int height)
    {
        MoveCalls.Add((hwnd, x, y, width, height));
        return NextResult;
    }

    public OperationResult SetWindowState(nint hwnd, WindowVisualState state)
    {
        StateCalls.Add((hwnd, state));
        return NextResult;
    }

    public OperationResult ActivateWindow(nint hwnd)
    {
        Activated.Add(hwnd);
        return NextResult;
    }

    /// <summary>Windows the rules asked to close, so a test can check what was and was not.</summary>
    public List<nint> Closed { get; } = new();

    /// <summary>Windows the rules brought to the front.</summary>
    public List<nint> Activated { get; } = new();

    public OperationResult CloseWindow(nint hwnd)
    {
        Closed.Add(hwnd);
        return NextResult;
    }

    /// <summary>Los monitores que finge tener el equipo. Uno por defecto.</summary>
    public List<MonitorInfo> Monitors { get; } = new()
    {
        new MonitorInfo(1, new WindowBounds(0, 0, 1920, 1080), new WindowBounds(0, 0, 1920, 1040), true)
    };

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    /// <summary>Dónde finge estar el ratón. El primero por defecto.</summary>
    public MonitorInfo? MonitorAtCursor { get; set; }

    public MonitorInfo? GetMonitorAtCursor() => MonitorAtCursor ?? Monitors.FirstOrDefault();

    /// <summary>Las ventanas que las reglas mandaron a otro monitor.</summary>
    public List<(nint Handle, int Monitor)> MonitorMoves { get; } = new();

    public OperationResult MoveWindowToMonitor(nint hwnd, int monitorNumber)
    {
        MonitorMoves.Add((hwnd, monitorNumber));
        return NextResult;
    }

    public OperationResult SetNonActivating(nint hwnd, bool enabled) => NextResult;

    public OperationResult SetClickThrough(nint hwnd, bool enabled) => NextResult;

    public OperationResult MoveWindowVisible(nint hwnd, int x, int y, int width, int height) =>
        MoveWindow(hwnd, x, y, width, height);

    /// <summary>Lo que cada ventana dice tener, para poder comprobar que se le devuelve.</summary>
    public Dictionary<nint, int> Corners { get; } = new();

    public int? GetCornerPreference(nint hwnd) => Corners.TryGetValue(hwnd, out int c) ? c : null;

    public OperationResult SetCornerPreference(nint hwnd, int preference)
    {
        if (!Corners.ContainsKey(hwnd))
        {
            return OperationResult.InvalidWindow();
        }

        Corners[hwnd] = preference;
        return NextResult;
    }

    public OperationResult SetSquareCorners(nint hwnd, bool enabled) =>
        SetCornerPreference(hwnd, enabled ? 1 : 0);

    /// <summary>Si cada ventana tiene ahora el filo puesto, para comprobar que se le repone.</summary>
    public Dictionary<nint, bool> Borders { get; } = new();

    public OperationResult SetBorderVisible(nint hwnd, bool visible)
    {
        Borders[hwnd] = visible;
        return NextResult;
    }

    public OperationResult PlaceOnTop(nint hwnd, int x, int y, int width, int height)
    {
        MoveCalls.Add((hwnd, x, y, width, height));
        return NextResult;
    }
}

public sealed class FakeVirtualDesktopService : IVirtualDesktopService
{
    public string BackendName => "Fake";

    public VirtualDesktopCapabilities Capabilities { get; set; } = VirtualDesktopCapabilities.None;

    /// <summary>Everything an advanced backend can do.</summary>
    public static VirtualDesktopCapabilities FullCapabilities { get; } = new()
    {
        CanGetWindowDesktop = true,
        CanMoveWindow = true,
        CanEnumerateDesktops = true,
        CanSwitchDesktop = true,
        CanCreateDesktop = true,
        CanRemoveDesktop = true,
        CanRenameDesktop = true,
        CanPinWindow = true,
        CanPinApplication = true
    };

    public List<VirtualDesktopInfo> Desktops { get; } = new();

    public Dictionary<nint, Guid> WindowDesktops { get; } = new();

    public Guid CurrentDesktop { get; set; }

    public List<(nint Handle, Guid Desktop)> Moves { get; } = new();

    public List<Guid> Switches { get; } = new();

    public Dictionary<nint, bool> Pinned { get; } = new();

    public OperationResult NextMoveResult { get; set; } = OperationResult.Ok();

    public static FakeVirtualDesktopService WithDesktops(int count)
    {
        FakeVirtualDesktopService service = new() { Capabilities = FullCapabilities };

        for (int i = 0; i < count; i++)
        {
            service.Desktops.Add(new VirtualDesktopInfo(
                Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"),
                i,
                i == 0 ? "Main" : string.Empty));
        }

        service.CurrentDesktop = service.Desktops[0].Id;
        return service;
    }

    public bool IsWindowOnCurrentDesktop(nint hwnd) =>
        IsWindowPinned(hwnd) == true || GetWindowDesktopId(hwnd) == CurrentDesktop;

    /// <summary>Null for a pinned window, which is on every desktop and so on none in particular.</summary>
    public Guid? GetWindowDesktopId(nint hwnd) =>
        IsWindowPinned(hwnd) == true ? null
        : WindowDesktops.TryGetValue(hwnd, out Guid id) ? id
        : null;

    public OperationResult MoveWindowToDesktop(nint hwnd, Guid desktopId)
    {
        if (!NextMoveResult.IsSuccess)
        {
            return NextMoveResult;
        }

        Moves.Add((hwnd, desktopId));
        WindowDesktops[hwnd] = desktopId;

        // Windows unpins the view as a side effect of moving it, answering S_OK all the same
        // (measured on build 26200). The fake has to do it too, or the tests would bless a
        // behaviour the real backend does not have.
        Pinned[hwnd] = false;

        return OperationResult.Ok();
    }

    /// <summary>Desktops the fake refused to create, to exercise a shell that does not answer.</summary>
    public int FailCreateAfter { get; set; } = int.MaxValue;

    public List<string> Renames { get; } = new();

    public VirtualDesktopInfo? CreateDesktop()
    {
        if (!Capabilities.CanCreateDesktop || Desktops.Count >= FailCreateAfter)
        {
            return null;
        }

        VirtualDesktopInfo created = new(Guid.NewGuid(), Desktops.Count, string.Empty);
        Desktops.Add(created);

        // Windows switches the view to the desktop it has just created, and the caller is
        // expected to put the user back. The fake does it too, or that would go untested.
        CurrentDesktop = created.Id;

        return created;
    }

    public OperationResult RemoveDesktop(Guid desktopId)
    {
        if (!Capabilities.CanRemoveDesktop)
        {
            return OperationResult.NotSupported("The fake cannot remove desktops.");
        }

        VirtualDesktopInfo? destination = Desktops.Count > 1
            ? VirtualDeskManager.Core.Desktops.DesktopRemoval.Destination(Desktops, desktopId)
            : null;

        if (destination is null)
        {
            return OperationResult.Fail("That desktop cannot be removed.");
        }

        Desktops.RemoveAll(d => d.Id == desktopId);

        for (int i = 0; i < Desktops.Count; i++)
        {
            Desktops[i] = Desktops[i] with { Index = i };
        }

        if (CurrentDesktop == desktopId)
        {
            CurrentDesktop = destination.Id;
        }

        return OperationResult.Ok();
    }

    public OperationResult SetDesktopName(Guid desktopId, string name)
    {
        if (!Capabilities.CanRenameDesktop)
        {
            return OperationResult.NotSupported("The fake cannot rename desktops.");
        }

        int index = Desktops.FindIndex(d => d.Id == desktopId);

        if (index < 0)
        {
            return OperationResult.Fail("That desktop does not exist any more.");
        }

        Desktops[index] = new VirtualDesktopInfo(desktopId, Desktops[index].Index, name);
        Renames.Add(name);
        return OperationResult.Ok();
    }

    public IReadOnlyList<VirtualDesktopInfo> GetDesktops() => Desktops;

    public VirtualDesktopInfo? GetCurrentDesktop() => Desktops.FirstOrDefault(d => d.Id == CurrentDesktop);

    public OperationResult SwitchToDesktop(Guid desktopId)
    {
        Switches.Add(desktopId);
        CurrentDesktop = desktopId;
        return OperationResult.Ok();
    }

    public bool? IsWindowPinned(nint hwnd) =>
        Capabilities.CanPinWindow ? Pinned.TryGetValue(hwnd, out bool pinned) && pinned : null;

    public OperationResult SetWindowPinned(nint hwnd, bool pinned)
    {
        if (!Capabilities.CanPinWindow)
        {
            return OperationResult.NotSupported();
        }

        Pinned[hwnd] = pinned;
        return OperationResult.Ok();
    }

    /// <summary>Aplicaciones ancladas, por el proceso de la ventana.</summary>
    /// <remarks>
    /// Por proceso y no por ventana, que es justo lo que distingue anclar una aplicación de
    /// anclar una ventana: alcanza también a las que esa aplicación abra después.
    /// </remarks>
    public Dictionary<string, bool> PinnedApplications { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool? IsApplicationPinned(nint hwnd)
    {
        if (!Capabilities.CanPinApplication)
        {
            return null;
        }

        string? app = ApplicationOf(hwnd);

        return app is null ? null : PinnedApplications.TryGetValue(app, out bool pinned) && pinned;
    }

    public OperationResult SetApplicationPinned(nint hwnd, bool pinned)
    {
        if (!Capabilities.CanPinApplication)
        {
            return OperationResult.NotSupported();
        }

        string? app = ApplicationOf(hwnd);

        if (app is null)
        {
            return OperationResult.Fail("No se sabe de que aplicacion es esa ventana.");
        }

        PinnedApplications[app] = pinned;

        return OperationResult.Ok();
    }

    /// <summary>De qué aplicación es una ventana. Lo pone la prueba.</summary>
    public Dictionary<nint, string> Applications { get; } = new();

    private string? ApplicationOf(nint hwnd) =>
        Applications.TryGetValue(hwnd, out string? app) ? app : null;
}

public sealed class FakeWindowEventService : IWindowEventService
{
    public event EventHandler<Core.Events.WindowDetectedEventArgs>? WindowCreated;
    public event EventHandler<Core.Events.WindowDetectedEventArgs>? WindowShown;
    public event EventHandler<Core.Events.WindowDetectedEventArgs>? ForegroundWindowChanged;
    public event EventHandler<Core.Events.WindowDetectedEventArgs>? WindowDestroyed;

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public void RaiseCreated(WindowInfo window) =>
        WindowCreated?.Invoke(
            this,
            new Core.Events.WindowDetectedEventArgs(window.Handle, Core.Events.WindowEventKind.Created, window));

    public void RaiseForeground(WindowInfo window) =>
        ForegroundWindowChanged?.Invoke(
            this,
            new Core.Events.WindowDetectedEventArgs(window.Handle, Core.Events.WindowEventKind.ForegroundChanged, window));

    public void RaiseDestroyed(nint handle) =>
        WindowDestroyed?.Invoke(
            this,
            new Core.Events.WindowDetectedEventArgs(handle, Core.Events.WindowEventKind.Destroyed, null));

    public void RaiseShown(WindowInfo window) =>
        WindowShown?.Invoke(
            this,
            new Core.Events.WindowDetectedEventArgs(window.Handle, Core.Events.WindowEventKind.Shown, window));

    public void RaiseForegroundChanged(WindowInfo window) =>
        ForegroundWindowChanged?.Invoke(
            this,
            new Core.Events.WindowDetectedEventArgs(window.Handle, Core.Events.WindowEventKind.ForegroundChanged, window));
}

public static class TestWindows
{
    public static WindowInfo Create(
        nint handle = 1,
        string processName = "notepad.exe",
        string title = "Untitled - Notepad",
        string className = "Notepad",
        bool topMost = false,
        string executablePath = @"C:\Windows\System32\notepad.exe",
        uint processId = 1234) =>
        new()
        {
            Handle = handle,
            ProcessId = processId,
            ProcessName = processName,
            ExecutablePath = executablePath,
            Title = title,
            ClassName = className,
            IsVisible = true,
            IsTopMost = topMost,
            Bounds = new WindowBounds(0, 0, 800, 600)
        };
}

/// <summary>Configuration held in memory, counting how many times it was written.</summary>
public sealed class FakeConfigurationService : IConfigurationService
{
    public ConfigurationPaths Paths { get; } = new(System.IO.Path.GetTempPath());

    public AppSettings Settings { get; set; } = new();

    public List<HotkeyDefinition> HotkeyList { get; } = new();

    public List<WindowRule> RuleList { get; } = new();

    public IReadOnlyList<HotkeyDefinition> Hotkeys => HotkeyList;

    public IReadOnlyList<WindowRule> Rules => RuleList;

    public IReadOnlyList<ConfigurationIssue> Issues => System.Array.Empty<ConfigurationIssue>();

    public int Saves { get; private set; }

    public event EventHandler? Reloaded;

    public void Load() { }

    public void Reload() => Reloaded?.Invoke(this, EventArgs.Empty);

    public void SaveSettings() => Saves++;

    public void SaveRules(IEnumerable<WindowRule> rules)
    {
        RuleList.Clear();
        RuleList.AddRange(rules);
    }

    public void SaveHotkeys(IEnumerable<HotkeyDefinition> hotkeys)
    {
        HotkeyList.Clear();
        HotkeyList.AddRange(hotkeys);
    }
}
