using System.Collections.Generic;
using VirtualDeskManager.Core.Localization;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>
/// Una de las tres posturas que una regla puede tomar ante una propiedad de la ventana.
/// </summary>
/// <remarks>
/// Sustituye a las casillas de tres estados, que eran la causa de dos fallos reportados como si
/// fueran del programa: en una casilla así, «vacío» no significa «me da igual» sino «desactívalo
/// a la fuerza», y la diferencia con el estado intermedio es un cuadrito gris que nadie
/// distingue. El resultado fue una regla que desfijaba Teams cada vez que cambiaba el foco y otra
/// que maximizaba Visual Studio Code, ambas sin que el usuario supiera que lo había pedido.
///
/// Con tres opciones escritas no hay nada que interpretar.
/// </remarks>
public sealed class RuleActionChoice
{
    private RuleActionChoice(bool? value, string key)
    {
        Value = value;
        Key = key;
    }

    /// <summary>Null es "la regla no opina sobre esto".</summary>
    public bool? Value { get; }

    private string Key { get; }

    /// <summary>Lo que se lee en el desplegable, en el idioma de la interfaz.</summary>
    public string Display => Loc.Get(Key);

    /// <summary>«No la toco»: la regla deja la propiedad como esté.</summary>
    public static RuleActionChoice Leave { get; } = new(null, "RuleEditor.ActionLeave");

    /// <summary>«Activar»: la regla la enciende.</summary>
    public static RuleActionChoice TurnOn { get; } = new(true, "RuleEditor.ActionOn");

    /// <summary>«Desactivar»: la regla la apaga.</summary>
    public static RuleActionChoice TurnOff { get; } = new(false, "RuleEditor.ActionOff");

    /// <summary>Las tres, en el orden en que se ofrecen: primero la que no hace nada.</summary>
    public static IReadOnlyList<RuleActionChoice> All { get; } = new[] { Leave, TurnOn, TurnOff };

    /// <summary>La opción que corresponde al valor guardado en la regla.</summary>
    public static RuleActionChoice For(bool? value) => value switch
    {
        null => Leave,
        true => TurnOn,
        false => TurnOff
    };

    /// <summary>Lo mismo que <see cref="Display"/>.</summary>
    public override string ToString() => Display;
}

/// <summary>Un monitor del equipo, para elegirlo de una lista en vez de escribir su número.</summary>
public sealed class MonitorChoice
{
    /// <summary>Crea la opción de un monitor.</summary>
    /// <param name="number">Su número, empezando en 1; cero es «ninguno».</param>
    /// <param name="display">Lo que se lee en la lista.</param>
    public MonitorChoice(int number, string display)
    {
        Number = number;
        Display = display;
    }

    /// <summary>El número que se guarda en la regla, empezando en 1. Cero es "ninguno".</summary>
    public int Number { get; }

    /// <summary>Lo que se lee en la lista.</summary>
    public string Display { get; }

    /// <summary>La opción de no decir nada sobre el monitor.</summary>
    public static MonitorChoice None { get; } = new(0, "");

    /// <summary>Es la opción de no decir nada sobre el monitor.</summary>
    public bool IsNone => Number == 0;

    /// <summary>Lo mismo que <see cref="Display"/>.</summary>
    public override string ToString() => Display;
}
