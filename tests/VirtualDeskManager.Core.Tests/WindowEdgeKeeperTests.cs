using VirtualDeskManager.Core.Zones;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// Quitar el redondeo y el filo de las ventanas encajadas, y devolvérselos después.
/// </summary>
/// <remarks>
/// Windows 11 pinta dos cosas alrededor de cada ventana. Medido en el equipo del usuario: Visual
/// Studio Code pide redondeo grande (2) y Teams se deja lo que diga Windows (0), y en la junta de
/// dos ventanas encajadas los pixeles del filo salian en #7C7D7B sobre un contenido de #121314.
/// Dos ventanas pegadas al pixel y aun asi una raya gris entre ellas.
/// </remarks>
public class WindowEdgeKeeperTests
{
    private const int Default = 0;
    private const int Square = 1;
    private const int Round = 2;

    private static (WindowEdgeKeeper Keeper, FakeWindowManager Windows) Build(params (nint Handle, int Corner)[] windows)
    {
        FakeWindowManager manager = new();

        foreach ((nint handle, int corner) in windows)
        {
            manager.Corners[handle] = corner;
        }

        return (new WindowEdgeKeeper(manager), manager);
    }

    [Fact]
    public void A_snapped_window_loses_its_rounding_and_its_edge()
    {
        (WindowEdgeKeeper keeper, FakeWindowManager windows) = Build((5, Round));

        keeper.MakeFlush(5);

        Assert.Equal(Square, windows.Corners[5]);
        Assert.False(windows.Borders[5]);
    }

    /// <summary>
    /// Cambiarle el aspecto a la ventana de otro programa y no deshacerlo sería dejar rastro en un
    /// sitio donde no nos han invitado.
    /// </summary>
    [Fact]
    public void Every_window_gets_its_own_corners_back()
    {
        (WindowEdgeKeeper keeper, FakeWindowManager windows) = Build((5, Round), (7, Default));

        keeper.MakeFlush(5);
        keeper.MakeFlush(7);
        keeper.RestoreAll();

        Assert.Equal(Round, windows.Corners[5]);
        Assert.Equal(Default, windows.Corners[7]);
        Assert.True(windows.Borders[5]);
        Assert.True(windows.Borders[7]);
        Assert.Equal(0, keeper.Count);
    }

    [Fact]
    public void One_window_can_be_given_its_corners_back_on_its_own()
    {
        (WindowEdgeKeeper keeper, FakeWindowManager windows) = Build((5, Round), (7, Round));

        keeper.MakeFlush(5);
        keeper.MakeFlush(7);
        keeper.Restore(5);

        Assert.Equal(Round, windows.Corners[5]);
        Assert.Equal(Square, windows.Corners[7]);
        Assert.Equal(1, keeper.Count);
    }

    /// <summary>
    /// Encajar dos veces la misma ventana no puede grabar el cuadrado como si fuera lo que tenía
    /// el usuario, o ya no habría a qué volver.
    /// </summary>
    [Fact]
    public void Snapping_the_same_window_twice_does_not_forget_what_it_had()
    {
        (WindowEdgeKeeper keeper, FakeWindowManager windows) = Build((5, Round));

        keeper.MakeFlush(5);
        keeper.MakeFlush(5);
        keeper.RestoreAll();

        Assert.Equal(Round, windows.Corners[5]);
    }

    [Fact]
    public void A_window_that_is_gone_is_not_remembered()
    {
        (WindowEdgeKeeper keeper, FakeWindowManager windows) = Build();

        keeper.MakeFlush(99);

        Assert.Equal(0, keeper.Count);
        Assert.Empty(windows.Corners);
    }

    [Fact]
    public void Restoring_a_window_that_was_never_squared_does_nothing()
    {
        (WindowEdgeKeeper keeper, FakeWindowManager windows) = Build((5, Round));

        keeper.Restore(5);

        Assert.Equal(Round, windows.Corners[5]);
    }
}
