using VirtualDeskManager.Core.Layout;
using VirtualDeskManager.Core.Models;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class DisplayFingerprintTests
{
    private static MonitorInfo Monitor(int x, int y, int width, int height, bool primary = false) =>
        new(1, new WindowBounds(x, y, width, height), new WindowBounds(x, y, width, height), primary);

    [Fact]
    public void The_same_screens_always_give_the_same_fingerprint()
    {
        MonitorInfo[] monitors = { Monitor(0, 0, 3440, 1440, primary: true), Monitor(-1920, 0, 1920, 1080) };

        Assert.Equal(DisplayFingerprint.Of(monitors), DisplayFingerprint.Of(monitors));
    }

    /// <summary>
    /// Windows no enumera los monitores en un orden estable, y si el orden entrara en la huella,
    /// la misma mesa de trabajo parecería dos sitios distintos y las ventanas no volverían.
    /// </summary>
    [Fact]
    public void The_order_windows_lists_them_in_does_not_matter()
    {
        MonitorInfo a = Monitor(0, 0, 3440, 1440, primary: true);
        MonitorInfo b = Monitor(-1920, 0, 1920, 1080);

        Assert.Equal(DisplayFingerprint.Of(new[] { a, b }), DisplayFingerprint.Of(new[] { b, a }));
    }

    /// <summary>Justo lo que pasa al suspender: se apagan los externos y queda uno.</summary>
    [Fact]
    public void Losing_a_screen_is_a_different_fingerprint()
    {
        MonitorInfo big = Monitor(0, 0, 3440, 1440, primary: true);
        MonitorInfo small = Monitor(-1920, 0, 1920, 1080);

        Assert.NotEqual(DisplayFingerprint.Of(new[] { big, small }), DisplayFingerprint.Of(new[] { big }));
    }

    [Fact]
    public void Changing_the_resolution_is_a_different_fingerprint()
    {
        Assert.NotEqual(
            DisplayFingerprint.Of(new[] { Monitor(0, 0, 3440, 1440, primary: true) }),
            DisplayFingerprint.Of(new[] { Monitor(0, 0, 1920, 1080, primary: true) }));
    }

    [Fact]
    public void Moving_a_screen_to_the_other_side_is_a_different_fingerprint()
    {
        Assert.NotEqual(
            DisplayFingerprint.Of(new[] { Monitor(0, 0, 1920, 1080, true), Monitor(-1920, 0, 1920, 1080) }),
            DisplayFingerprint.Of(new[] { Monitor(0, 0, 1920, 1080, true), Monitor(1920, 0, 1920, 1080) }));
    }

    [Fact]
    public void No_screens_at_all_is_not_a_fingerprint_to_save_anything_under()
    {
        Assert.False(DisplayFingerprint.Of(Array.Empty<MonitorInfo>()).IsKnown);
    }
}

public class WindowLayoutStoreTests
{
    private static WindowInfo Window(
        nint handle,
        string process,
        string title,
        int x,
        int y,
        string className = "Window",
        bool minimized = false,
        bool maximized = false) =>
        new()
        {
            Handle = handle,
            ProcessName = process,
            ClassName = className,
            Title = title,
            Bounds = new WindowBounds(x, y, 800, 600),
            IsVisible = true,
            IsMinimized = minimized,
            IsMaximized = maximized
        };

    private static WindowPlacement Placement(
        string process,
        string title,
        int x,
        int y,
        string className = "Window",
        WindowVisualState state = WindowVisualState.Normal) =>
        new(process, className, title, new WindowBounds(x, y, 800, 600), state);

    private static WindowLayoutSnapshot Snapshot(params WindowPlacement[] placements) =>
        new(new DisplayFingerprint("prueba"), DateTimeOffset.Now, placements);

    [Fact]
    public void A_window_goes_back_to_where_it_was()
    {
        WindowLayoutSnapshot snapshot = Snapshot(Placement("code.exe", "proyecto", -1920, 0));
        WindowInfo squashed = Window(5, "code.exe", "proyecto", 0, 0);

        PlacementRestore restore = Assert.Single(WindowLayoutStore.Match(snapshot, new[] { squashed }));

        Assert.Equal(5, restore.Handle);
        Assert.Equal(-1920, restore.Placement.Bounds.X);
    }

    /// <summary>
    /// El título cambia solo: un editor pone el fichero abierto y un navegador la pestaña. Exigir
    /// que coincida dejaría sin recolocar justo las ventanas en las que uno estaba trabajando.
    /// </summary>
    [Fact]
    public void A_window_whose_title_changed_is_still_recognised()
    {
        WindowLayoutSnapshot snapshot = Snapshot(Placement("code.exe", "ayer.cs", -1920, 0));
        WindowInfo today = Window(5, "code.exe", "hoy.cs", 0, 0);

        Assert.Single(WindowLayoutStore.Match(snapshot, new[] { today }));
    }

    /// <summary>
    /// Con varias ventanas del mismo programa, cada una tiene que volver a la suya. Por eso el
    /// emparejamiento va de lo específico a lo general y no al revés.
    /// </summary>
    [Fact]
    public void Several_windows_of_one_program_each_go_back_to_their_own_place()
    {
        WindowLayoutSnapshot snapshot = Snapshot(
            Placement("code.exe", "izquierda", -1920, 0),
            Placement("code.exe", "derecha", 1720, 0));

        WindowInfo left = Window(1, "code.exe", "izquierda", 0, 0);
        WindowInfo right = Window(2, "code.exe", "derecha", 0, 0);

        IReadOnlyList<PlacementRestore> restores = WindowLayoutStore.Match(snapshot, new[] { left, right });

        Assert.Equal(2, restores.Count);
        Assert.Equal(-1920, restores.Single(r => r.Handle == 1).Placement.Bounds.X);
        Assert.Equal(1720, restores.Single(r => r.Handle == 2).Placement.Bounds.X);
    }

    [Fact]
    public void One_window_is_never_used_for_two_saved_places()
    {
        WindowLayoutSnapshot snapshot = Snapshot(
            Placement("code.exe", "uno", -1920, 0),
            Placement("code.exe", "dos", 1720, 0));

        IReadOnlyList<PlacementRestore> restores =
            WindowLayoutStore.Match(snapshot, new[] { Window(1, "code.exe", "uno", 0, 0) });

        Assert.Single(restores);
    }

    [Fact]
    public void A_window_that_was_not_open_before_is_left_alone()
    {
        WindowLayoutSnapshot snapshot = Snapshot(Placement("code.exe", "proyecto", -1920, 0));

        Assert.Empty(WindowLayoutStore.Match(snapshot, new[] { Window(9, "spotify.exe", "musica", 0, 0) }));
    }

    /// <summary>
    /// Tras una suspensión las ventanas no vuelven minimizadas, vuelven amontonadas. Si una está
    /// minimizada es porque la minimizó el usuario, y sacársela a la cara sería lo contrario de
    /// lo que espera.
    /// </summary>
    [Fact]
    public void A_minimised_window_is_not_pulled_back_out()
    {
        WindowLayoutSnapshot snapshot = Snapshot(Placement("code.exe", "proyecto", -1920, 0));
        WindowInfo minimised = Window(5, "code.exe", "proyecto", 0, 0, minimized: true);

        Assert.Empty(WindowLayoutStore.Match(snapshot, new[] { minimised }));
    }

    [Fact]
    public void A_window_already_in_its_place_is_not_touched()
    {
        WindowLayoutSnapshot snapshot = Snapshot(Placement("code.exe", "proyecto", -1920, 0));
        WindowInfo alreadyThere = Window(5, "code.exe", "proyecto", -1920, 0);

        Assert.Empty(WindowLayoutStore.Match(snapshot, new[] { alreadyThere }));
    }

    [Fact]
    public void A_window_in_the_right_place_but_maximised_when_it_was_not_does_get_fixed()
    {
        WindowLayoutSnapshot snapshot = Snapshot(Placement("code.exe", "proyecto", -1920, 0));
        WindowInfo maximised = Window(5, "code.exe", "proyecto", -1920, 0, maximized: true);

        Assert.Single(WindowLayoutStore.Match(snapshot, new[] { maximised }));
    }

    // ------------------------------------------------------------------ historial

    [Fact]
    public void Saving_again_with_the_same_screens_replaces_instead_of_piling_up()
    {
        DisplayFingerprint office = new("oficina");

        IReadOnlyList<WindowLayoutSnapshot> stored = WindowLayoutStore.Remember(
            Array.Empty<WindowLayoutSnapshot>(),
            new WindowLayoutSnapshot(office, DateTimeOffset.Now.AddHours(-1), Array.Empty<WindowPlacement>()));

        stored = WindowLayoutStore.Remember(
            stored,
            new WindowLayoutSnapshot(office, DateTimeOffset.Now, new[] { Placement("code.exe", "x", 0, 0) }));

        WindowLayoutSnapshot only = Assert.Single(stored);
        Assert.Single(only.Placements);
    }

    [Fact]
    public void Different_screens_are_kept_side_by_side()
    {
        IReadOnlyList<WindowLayoutSnapshot> stored = WindowLayoutStore.Remember(
            Array.Empty<WindowLayoutSnapshot>(),
            new WindowLayoutSnapshot(new DisplayFingerprint("oficina"), DateTimeOffset.Now, Array.Empty<WindowPlacement>()));

        stored = WindowLayoutStore.Remember(
            stored,
            new WindowLayoutSnapshot(new DisplayFingerprint("casa"), DateTimeOffset.Now, Array.Empty<WindowPlacement>()));

        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public void Only_the_most_recent_places_are_remembered()
    {
        IReadOnlyList<WindowLayoutSnapshot> stored = Array.Empty<WindowLayoutSnapshot>();

        for (int i = 0; i < WindowLayoutStore.MaximumRememberedLayouts + 4; i++)
        {
            stored = WindowLayoutStore.Remember(
                stored,
                new WindowLayoutSnapshot(
                    new DisplayFingerprint("sitio" + i),
                    DateTimeOffset.Now.AddMinutes(i),
                    Array.Empty<WindowPlacement>()));
        }

        Assert.Equal(WindowLayoutStore.MaximumRememberedLayouts, stored.Count);
        Assert.Contains(stored, snapshot => snapshot.Fingerprint.Value == "sitio11");
        Assert.DoesNotContain(stored, snapshot => snapshot.Fingerprint.Value == "sitio0");
    }

    [Fact]
    public void Looking_up_screens_that_were_never_seen_finds_nothing()
    {
        IReadOnlyList<WindowLayoutSnapshot> stored = WindowLayoutStore.Remember(
            Array.Empty<WindowLayoutSnapshot>(),
            new WindowLayoutSnapshot(new DisplayFingerprint("oficina"), DateTimeOffset.Now, Array.Empty<WindowPlacement>()));

        Assert.Null(WindowLayoutStore.Find(stored, new DisplayFingerprint("tren")));
        Assert.Null(WindowLayoutStore.Find(stored, DisplayFingerprint.Unknown));
        Assert.NotNull(WindowLayoutStore.Find(stored, new DisplayFingerprint("oficina")));
    }
}
