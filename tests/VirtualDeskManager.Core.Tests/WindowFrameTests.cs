using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Zones;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// El borde invisible de las ventanas de Windows 11.
/// </summary>
/// <remarks>
/// Medido en el equipo del usuario con Visual Studio Code, Edge, Teams y la terminal: siete
/// píxeles a la izquierda, siete a la derecha, siete abajo y ninguno arriba. Por eso poner dos
/// ventanas en zonas contiguas dejaba catorce píxeles de hueco aunque la separación estuviera a
/// cero.
/// </remarks>
public class WindowFrameTests
{
    /// <summary>El caso real, con los márgenes tal y como se midieron.</summary>
    private static WindowBounds Adjust(WindowBounds target, int left = 7, int top = 0, int right = 7, int bottom = 7)
    {
        WindowBounds windowRect = new(100, 100, 800, 600);

        WindowBounds visible = new(
            windowRect.X + left,
            windowRect.Y + top,
            windowRect.Width - left - right,
            windowRect.Height - top - bottom);

        return WindowFrame.Adjust(target, windowRect, visible);
    }

    /// <summary>
    /// Los numeros exactos que se midieron en el equipo del usuario el 2026-09-24, con dos
    /// ventanas de Visual Studio Code encajadas en las dos mitades del monitor izquierdo.
    /// </summary>
    /// <remarks>
    /// Sin corregir quedaba asi, que es lo que el usuario marco con puntos rojos en su captura:
    ///
    ///   zona -1920..-960  -> ventana -1920..-960, visible -1913..-967
    ///   zona  -960..0     -> ventana  -960..0,    visible  -953..-7
    ///
    /// Siete pixeles perdidos contra el borde de la pantalla y catorce entre las dos ventanas.
    /// Esta prueba fija que con la correccion los bordes visibles caen justo donde dice la zona.
    /// </remarks>
    [Fact]
    public void The_measured_case_from_the_users_machine_comes_out_flush()
    {
        // Como estaban al medir: el rectangulo pegado a la zona y lo visible metido hacia dentro.
        WindowBounds leftWindow = new(-1920, 357, 960, 1039);
        WindowBounds leftVisible = new(-1913, 357, 946, 1032);

        WindowBounds rightWindow = new(-960, 357, 960, 1039);
        WindowBounds rightVisible = new(-953, 357, 946, 1032);

        WindowBounds leftZone = new(-1920, 357, 960, 1039);
        WindowBounds rightZone = new(-960, 357, 960, 1039);

        WindowBounds askedLeft = WindowFrame.Adjust(leftZone, leftWindow, leftVisible);
        WindowBounds askedRight = WindowFrame.Adjust(rightZone, rightWindow, rightVisible);

        // Lo que se vera de cada una, con los mismos siete pixeles de franja invisible.
        int seenLeftRight = askedLeft.Right - 7;
        int seenRightLeft = askedRight.X + 7;
        int seenLeftLeft = askedLeft.X + 7;
        int seenRightRight = askedRight.Right - 7;

        Assert.Equal(-1920, seenLeftLeft);      // pegada al borde de la pantalla
        Assert.Equal(-960, seenLeftRight);      // justo en la mitad
        Assert.Equal(-960, seenRightLeft);      // y la otra empieza ahi mismo
        Assert.Equal(0, seenRightRight);        // pegada al borde derecho
        Assert.Equal(seenLeftRight, seenRightLeft);
    }

    [Fact]
    public void The_window_is_grown_so_that_what_you_see_lands_on_the_zone()
    {
        WindowBounds zone = new(0, 0, 1000, 1400);

        WindowBounds asked = Adjust(zone);

        Assert.Equal(-7, asked.X);
        Assert.Equal(0, asked.Y);
        Assert.Equal(1014, asked.Width);
        Assert.Equal(1407, asked.Height);
    }

    /// <summary>
    /// Lo que de verdad importa: dos zonas pegadas tienen que dar dos ventanas pegadas.
    /// </summary>
    [Fact]
    public void Two_windows_in_touching_zones_end_up_touching()
    {
        WindowBounds left = new(0, 0, 1000, 1400);
        WindowBounds right = new(1000, 0, 1000, 1400);

        WindowBounds askedLeft = Adjust(left);
        WindowBounds askedRight = Adjust(right);

        // Lo que se ve de cada una es la zona exacta, asi que el borde visible de la izquierda
        // toca el de la derecha.
        Assert.Equal(left.Right, askedLeft.Right - 7);
        Assert.Equal(right.X, askedRight.X + 7);
        Assert.Equal(askedLeft.Right - 7, askedRight.X + 7);
    }

    [Fact]
    public void A_window_with_no_invisible_border_is_left_exactly_where_it_was_asked()
    {
        WindowBounds zone = new(0, 0, 1000, 1400);

        Assert.Equal(zone, Adjust(zone, left: 0, top: 0, right: 0, bottom: 0));
    }

    /// <summary>
    /// Arriba no hay franja invisible, así que la ventana no puede subir: hacerlo la metería por
    /// debajo de la barra de tareas si estuviera arriba, o fuera de la pantalla.
    /// </summary>
    [Fact]
    public void The_top_edge_is_not_moved_because_there_is_nothing_to_compensate_there()
    {
        WindowBounds asked = Adjust(new WindowBounds(0, 50, 1000, 1400));

        Assert.Equal(50, asked.Y);
    }

    [Fact]
    public void A_measurement_that_makes_no_sense_leaves_the_target_untouched()
    {
        WindowBounds zone = new(0, 0, 1000, 1400);
        WindowBounds windowRect = new(100, 100, 800, 600);

        // Lo visible mas grande que la ventana: solo puede venir de una medida tomada a mitad de
        // una animacion.
        WindowBounds impossible = new(90, 90, 900, 700);

        Assert.Equal(zone, WindowFrame.Adjust(zone, windowRect, impossible));
    }

    [Fact]
    public void Without_a_measurement_at_all_the_target_is_used_as_is()
    {
        WindowBounds zone = new(0, 0, 1000, 1400);

        Assert.Equal(zone, WindowFrame.Adjust(zone, default, default));
    }
}
