using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Taskbar;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>
/// Qué distintivo le toca a cada aplicación, y dónde cae dentro del botón.
/// </summary>
/// <remarks>
/// El riesgo de esta función no es que no se vea: eso se nota al instante. Es que diga el
/// escritorio equivocado, o que se salga del botón y marque a la aplicación de al lado, y ninguna
/// de las dos cosas se ve mirando la barra por encima.
/// </remarks>
public class TaskbarBadgeTests
{
    private static TaskbarBadgeSettings Settings() => new() { Enabled = true };

    /// <summary>Un botón de la barra de tareas de este equipo: 44 de ancho, 48 de alto.</summary>
    private static readonly WindowBounds Boton = new(1809, 1392, 44, 48);

    // ------------------------------------------------------------------ que se dice

    [Fact]
    public void A_window_on_another_desktop_shows_its_number()
    {
        Assert.Equal("3", TaskbarBadgePolicy.For(new[] { 3 }, pinned: false, current: 1, Settings()).Text);
    }

    /// <summary>
    /// Lo que hace la función útil en vez de ruidosa: un distintivo significa siempre "esto no
    /// está aquí". Si se marcara todo, la barra se llenaría de números que repiten el escritorio
    /// en el que ya estás.
    /// </summary>
    [Fact]
    public void Nothing_is_marked_on_the_desktop_you_are_already_on()
    {
        Assert.False(TaskbarBadgePolicy.For(new[] { 2 }, pinned: false, current: 2, Settings()).ShouldShow);
    }

    [Fact]
    public void Marking_the_current_desktop_can_be_asked_for()
    {
        TaskbarBadgeSettings settings = new() { Enabled = true, HideWhenCurrent = false };

        Assert.Equal("2", TaskbarBadgePolicy.For(new[] { 2 }, pinned: false, current: 2, settings).Text);
    }

    /// <summary>
    /// El caso normal y no el raro: una aplicación con ventanas repartidas. Elegir uno de los
    /// números sería mentir sobre los demás.
    /// </summary>
    [Fact]
    public void Windows_spread_over_several_desktops_say_so()
    {
        Assert.Equal(
            TaskbarBadgePolicy.Several,
            TaskbarBadgePolicy.For(new[] { 1, 4 }, pinned: false, current: 1, Settings()).Text);
    }

    [Fact]
    public void The_same_desktop_repeated_is_still_one_desktop()
    {
        Assert.Equal("4", TaskbarBadgePolicy.For(new[] { 4, 4, 4 }, pinned: false, current: 1, Settings()).Text);
    }

    /// <summary>Anclada se ve en todos, así que un número ahí sería falso.</summary>
    [Fact]
    public void A_pinned_application_is_marked_as_being_everywhere()
    {
        Assert.Equal(
            TaskbarBadgePolicy.Everywhere,
            TaskbarBadgePolicy.For(new[] { 2 }, pinned: true, current: 2, Settings()).Text);
    }

    /// <summary>Un icono anclado sin ventanas abiertas: no se sabe nada, no se dice nada.</summary>
    [Fact]
    public void An_application_that_is_not_running_gets_no_badge()
    {
        Assert.False(TaskbarBadgePolicy.For(Array.Empty<int>(), pinned: false, current: 1, Settings()).ShouldShow);
    }

    [Fact]
    public void Beyond_nine_desktops_a_digit_no_longer_fits()
    {
        Assert.Equal(
            TaskbarBadgePolicy.Beyond,
            TaskbarBadgePolicy.For(new[] { 12 }, pinned: false, current: 1, Settings()).Text);
    }

    [Fact]
    public void Desktops_that_are_not_known_are_ignored()
    {
        Assert.Equal("5", TaskbarBadgePolicy.For(new[] { 0, 5 }, pinned: false, current: 1, Settings()).Text);
    }

    // ------------------------------------------------------------------ donde cae

    [Fact]
    public void The_badge_sits_inside_the_button()
    {
        WindowBounds badge = TaskbarBadgeLayout.Place(Boton, Settings());

        Assert.True(badge.X >= Boton.X, "se sale por la izquierda");
        Assert.True(badge.Right <= Boton.Right, "se sale por la derecha, encima del vecino");
        Assert.True(badge.Y >= Boton.Y, "se sale por arriba");
        Assert.True(badge.Bottom <= Boton.Bottom, "se sale por abajo");
    }

    /// <summary>
    /// Doscientos por ciento sobre un botón de 44 píxeles: el usuario puede pedir lo que quiera,
    /// pero salirse del botón marcaría la aplicación de al lado.
    /// </summary>
    [Fact]
    public void An_oversized_badge_still_does_not_reach_the_neighbour()
    {
        TaskbarBadgeSettings settings = new() { Enabled = true, SizePercent = 200 };

        WindowBounds badge = TaskbarBadgeLayout.Place(Boton, settings);

        Assert.True(badge.Right <= Boton.Right);
        Assert.True(badge.Bottom <= Boton.Bottom);
        Assert.True(badge.Width <= Boton.Width);
    }

    [Theory]
    [InlineData(BadgeCorner.TopLeft)]
    [InlineData(BadgeCorner.TopRight)]
    [InlineData(BadgeCorner.BottomLeft)]
    [InlineData(BadgeCorner.BottomRight)]
    public void Every_corner_stays_within_the_button(BadgeCorner corner)
    {
        TaskbarBadgeSettings settings = new() { Enabled = true, Corner = corner };

        WindowBounds badge = TaskbarBadgeLayout.Place(Boton, settings);

        Assert.True(badge.X >= Boton.X && badge.Right <= Boton.Right);
        Assert.True(badge.Y >= Boton.Y && badge.Bottom <= Boton.Bottom);
    }

    [Fact]
    public void The_corners_are_actually_different_places()
    {
        WindowBounds arriba = TaskbarBadgeLayout.Place(
            Boton, new TaskbarBadgeSettings { Corner = BadgeCorner.TopLeft });

        WindowBounds abajo = TaskbarBadgeLayout.Place(
            Boton, new TaskbarBadgeSettings { Corner = BadgeCorner.BottomRight });

        Assert.True(abajo.X > arriba.X);
        Assert.True(abajo.Y > arriba.Y);
    }

    /// <summary>Una barra de tareas pequeña: manda el botón, no lo que se pidió.</summary>
    [Fact]
    public void On_a_tiny_button_the_badge_shrinks_to_fit()
    {
        WindowBounds diminuto = new(0, 0, 8, 8);

        WindowBounds badge = TaskbarBadgeLayout.Place(diminuto, new TaskbarBadgeSettings());

        Assert.True(badge.Width <= 8);
        Assert.True(badge.Height <= 8);
        Assert.True(badge.Right <= diminuto.Right);
    }

    [Fact]
    public void The_size_the_user_asks_for_is_honoured_where_it_fits()
    {
        int pequeno = TaskbarBadgeLayout.ComputeSize(48, 50);
        int grande = TaskbarBadgeLayout.ComputeSize(48, 200);

        Assert.True(grande > pequeno);
        Assert.InRange(pequeno, TaskbarBadgeLayout.MinimumSize, TaskbarBadgeLayout.MaximumSize);
        Assert.InRange(grande, TaskbarBadgeLayout.MinimumSize, TaskbarBadgeLayout.MaximumSize);
    }

    // ------------------------------------------------------------------ ajustes

    [Fact]
    public void Nonsense_settings_are_tidied_up()
    {
        TaskbarBadgeSettings settings = new() { SizePercent = 9000, OpacityPercent = -4, Colour = "no-es-un-color" };

        settings.Normalise();

        Assert.InRange(settings.SizePercent, 50, 200);
        Assert.InRange(settings.OpacityPercent, 20, 100);
        Assert.True(RgbColour.TryParse(settings.Colour, out _));
    }
}
