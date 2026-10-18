using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.Core.Desktops;

/// <summary>
/// Lo que hay que saber antes de eliminar un escritorio: si se puede, adónde irán sus ventanas y
/// qué automatizaciones se quedan sin él.
/// </summary>
/// <remarks>
/// Aritmética pura, para que los casos raros (el primero, el último que queda, una automatización
/// que lo nombra por número) los fijen los tests y no se descubran eliminando escritorios de
/// verdad.
/// </remarks>
public static class DesktopRemoval
{
    /// <summary>Si ese escritorio se puede eliminar: tiene que existir y no ser el único.</summary>
    /// <remarks>Windows siempre necesita al menos un escritorio.</remarks>
    public static bool CanRemove(IReadOnlyList<VirtualDesktopInfo> desktops, Guid desktopId)
    {
        ArgumentNullException.ThrowIfNull(desktops);
        return desktops.Count > 1 && desktops.Any(d => d.Id == desktopId);
    }

    /// <summary>
    /// El escritorio al que pasan las ventanas del que se elimina: el de su izquierda, y si es el
    /// primero, el de su derecha. Es lo que hace Windows al cerrar un escritorio.
    /// </summary>
    /// <returns>Null si no se puede eliminar.</returns>
    public static VirtualDesktopInfo? Destination(IReadOnlyList<VirtualDesktopInfo> desktops, Guid desktopId)
    {
        if (!CanRemove(desktops, desktopId))
        {
            return null;
        }

        int index = desktops.ToList().FindIndex(d => d.Id == desktopId);
        return desktops[index > 0 ? index - 1 : 1];
    }

    /// <summary>
    /// Las automatizaciones que llevan aplicaciones a ese escritorio, se refieran a él por nombre,
    /// por número o por identificador.
    /// </summary>
    /// <remarks>
    /// Por número también cuenta, y con intención: al eliminar el 2, «2» pasa a ser el que antes
    /// era el 3, y esa automatización empezaría a mandar las ventanas a otro sitio sin decir nada.
    /// </remarks>
    public static IReadOnlyList<WindowRule> RulesUsing(
        IEnumerable<WindowRule> rules,
        IReadOnlyList<VirtualDesktopInfo> desktops,
        Guid desktopId)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(desktops);

        return rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Actions?.Desktop)
                        && DesktopResolver.Resolve(r.Actions!.Desktop, desktops)?.Id == desktopId)
            .ToList();
    }
}
