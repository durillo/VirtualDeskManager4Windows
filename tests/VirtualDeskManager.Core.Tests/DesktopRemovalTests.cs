using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

/// <summary>Eliminar un escritorio: si se puede, adónde van sus ventanas y qué automatizaciones lo usan.</summary>
public class DesktopRemovalTests
{
    private static readonly VirtualDesktopInfo Trabajo = new(Guid.NewGuid(), 0, "Trabajo");
    private static readonly VirtualDesktopInfo Correo = new(Guid.NewGuid(), 1, "Correo");
    private static readonly VirtualDesktopInfo SinNombre = new(Guid.NewGuid(), 2, "");

    private static readonly IReadOnlyList<VirtualDesktopInfo> Tres = new[] { Trabajo, Correo, SinNombre };

    [Fact]
    public void El_unico_escritorio_no_se_puede_eliminar()
    {
        Assert.False(DesktopRemoval.CanRemove(new[] { Trabajo }, Trabajo.Id));
        Assert.Null(DesktopRemoval.Destination(new[] { Trabajo }, Trabajo.Id));
    }

    [Fact]
    public void Uno_que_no_existe_tampoco()
    {
        Assert.False(DesktopRemoval.CanRemove(Tres, Guid.NewGuid()));
    }

    [Fact]
    public void Las_ventanas_pasan_al_de_la_izquierda()
    {
        Assert.Equal(Correo, DesktopRemoval.Destination(Tres, SinNombre.Id));
        Assert.Equal(Trabajo, DesktopRemoval.Destination(Tres, Correo.Id));
    }

    [Fact]
    public void Si_es_el_primero_pasan_al_de_la_derecha()
    {
        Assert.Equal(Correo, DesktopRemoval.Destination(Tres, Trabajo.Id));
    }

    [Fact]
    public void Cuenta_las_automatizaciones_que_lo_nombran_por_nombre_numero_o_identificador()
    {
        WindowRule[] reglas =
        {
            Regla("por nombre", "correo"),
            Regla("por número", "2"),
            Regla("por identificador", Correo.Id.ToString()),
            Regla("otro escritorio", "Trabajo"),
            Regla("sin escritorio", null),
            Regla("uno que no existe", "Ocio")
        };

        IReadOnlyList<WindowRule> usan = DesktopRemoval.RulesUsing(reglas, Tres, Correo.Id);

        Assert.Equal(new[] { "por nombre", "por número", "por identificador" }, usan.Select(r => r.Name));
    }

    private static WindowRule Regla(string nombre, string? escritorio) =>
        new() { Name = nombre, Actions = new WindowActions { Desktop = escritorio } };
}
