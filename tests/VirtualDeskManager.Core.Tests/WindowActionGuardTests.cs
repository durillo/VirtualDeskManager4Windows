using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Loop protection of the rule engine (spec section 23).</summary>
public class WindowActionGuardTests
{
    [Fact]
    public void The_first_application_is_allowed()
    {
        WindowActionGuard guard = new();

        Assert.True(guard.TryBegin(1, "top:True"));
    }

    [Fact]
    public void A_second_call_while_one_is_in_flight_is_blocked()
    {
        WindowActionGuard guard = new();

        Assert.True(guard.TryBegin(1, "top:True"));
        Assert.False(guard.TryBegin(1, "top:True"));
    }

    /// <summary>
    /// Una regla se aplica una vez por ventana y no vuelve a imponerse, por mucho tiempo que
    /// pase.
    /// </summary>
    /// <remarks>
    /// Antes esto caducaba a los dos segundos, y como las reglas se reevalúan al cambiar el foco,
    /// una regla que dijera "maximizada" volvía a maximizar la ventana cada vez que el usuario la
    /// pulsaba. No había forma de dejarla pequeña.
    /// </remarks>
    [Fact]
    public void The_same_actions_are_never_applied_twice_to_the_same_window()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WindowActionGuard guard = new(() => now);

        Assert.True(guard.TryBegin(1, "top:True"));
        guard.End(1);

        now = now.AddMilliseconds(500);
        Assert.False(guard.TryBegin(1, "top:True"));

        now = now.AddSeconds(3);
        Assert.False(guard.TryBegin(1, "top:True"));

        // Ni una hora después: la ventana ya recibió lo suyo.
        now = now.AddHours(1);
        Assert.False(guard.TryBegin(1, "top:True"));
    }

    /// <summary>Cambiar la regla sí vuelve a aplicarla: la firma es otra.</summary>
    [Fact]
    public void Different_actions_are_applied_immediately()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WindowActionGuard guard = new(() => now);

        Assert.True(guard.TryBegin(1, "top:True"));
        guard.End(1);

        Assert.True(guard.TryBegin(1, "top:False"));
    }

    [Fact]
    public void Different_windows_do_not_block_each_other()
    {
        WindowActionGuard guard = new();

        Assert.True(guard.TryBegin(1, "top:True"));
        Assert.True(guard.TryBegin(2, "top:True"));
    }

    [Fact]
    public void Forgetting_a_window_clears_its_history()
    {
        DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WindowActionGuard guard = new(() => now);

        Assert.True(guard.TryBegin(1, "top:True"));
        guard.End(1);
        guard.Forget(1);

        Assert.True(guard.TryBegin(1, "top:True"));
    }
}
