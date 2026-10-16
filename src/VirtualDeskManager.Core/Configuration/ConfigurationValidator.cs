using System.Text.RegularExpressions;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>Everything that can be wrong with a shortcut or a rule being edited.</summary>
public enum ValidationIssue
{
    /// <summary>El atajo no tiene identificador.</summary>
    HotkeyEmptyId,
    /// <summary>Otro atajo ya usa ese identificador (sin distinguir mayúsculas).</summary>
    HotkeyDuplicateId,
    /// <summary>Falta la tecla principal.</summary>
    HotkeyNeedsKey,
    /// <summary>
    /// Aviso, no rechazo: una combinación sin Ctrl, Alt, Shift ni Win le quita esa tecla a todos
    /// los demás programas del equipo.
    /// </summary>
    HotkeyNeedsModifier,
    /// <summary>El atajo no tiene ninguna orden asignada.</summary>
    HotkeyNoCommand,
    /// <summary>Otro atajo ya reclama alguna de sus combinaciones.</summary>
    HotkeyDuplicateCombination,

    /// <summary>Las dos combinaciones del mismo atajo son la misma, que no sirve de nada.</summary>
    HotkeySameCombinationTwice,

    /// <summary>Not a refusal: a heads up that Windows keeps most Win+key combinations.</summary>
    HotkeyWindowsKeyOftenReserved,

    /// <summary>La regla no tiene nombre.</summary>
    RuleNeedsName,
    /// <summary>La regla no tiene ningún criterio, así que nunca encajaría con ninguna ventana.</summary>
    RuleNeedsCriteria,
    /// <summary>La regla no tiene ninguna acción.</summary>
    RuleNeedsActions,
    /// <summary>La expresión regular del título no se puede compilar.</summary>
    RuleInvalidRegex
}

/// <summary>
/// Checks what the user types in the editors before it reaches disk.
/// </summary>
/// <remarks>
/// Returns issues, not messages: turning them into text is the job of the interface, so Core
/// stays free of user visible strings and this can be unit tested (spec section 40).
/// </remarks>
public static class ConfigurationValidator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Lo que impide guardar un atajo: falta de id, tecla u orden, las dos combinaciones iguales, y
    /// choques de id o de combinación con los demás. Lista vacía si se puede guardar.
    /// </summary>
    /// <param name="hotkey">El atajo que se está editando.</param>
    /// <param name="others">Los demás atajos. El propio <paramref name="hotkey"/> puede ir dentro: se salta por referencia.</param>
    public static IReadOnlyList<ValidationIssue> Validate(
        HotkeyDefinition hotkey,
        IEnumerable<HotkeyDefinition>? others = null)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        List<ValidationIssue> issues = new();

        if (string.IsNullOrWhiteSpace(hotkey.Id))
        {
            issues.Add(ValidationIssue.HotkeyEmptyId);
        }

        if (hotkey.Key == HotkeyKey.None)
        {
            issues.Add(ValidationIssue.HotkeyNeedsKey);
        }

        if (hotkey.Command.Type == CommandType.None)
        {
            issues.Add(ValidationIssue.HotkeyNoCommand);
        }

        // Poner la misma combinación en las dos casillas no es un choque con nadie, pero tampoco
        // es lo que el usuario quería: creería que tiene dos maneras de lanzarlo y tendría una.
        if (hotkey.Alternate.IsSet && hotkey.Primary.SameAs(hotkey.Alternate))
        {
            issues.Add(ValidationIssue.HotkeySameCombinationTwice);
        }

        foreach (HotkeyDefinition other in others ?? Enumerable.Empty<HotkeyDefinition>())
        {
            if (ReferenceEquals(other, hotkey))
            {
                continue;
            }

            if (string.Equals(other.Id, hotkey.Id, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(ValidationIssue.HotkeyDuplicateId);
            }

            if (SameCombination(other, hotkey))
            {
                issues.Add(ValidationIssue.HotkeyDuplicateCombination);
            }
        }

        return issues.Distinct().ToList();
    }

    /// <summary>
    /// Things worth telling the user about a shortcut that are not reasons to refuse it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Validate(HotkeyDefinition, IEnumerable{HotkeyDefinition})"/>
    /// because the two answer different questions. That one decides whether the shortcut can be
    /// saved at all; this one decides what the user ought to know before they do. Mixing them is
    /// how "you should think twice about this" turns into "you may not do this".
    /// </remarks>
    public static IReadOnlyList<ValidationIssue> Warnings(HotkeyDefinition hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        List<ValidationIssue> warnings = new();

        // Las dos combinaciones, no sólo la primera: una segunda combinación mal elegida se queda
        // sin registrar igual que una primera, y callarlo sería peor porque se ve menos.
        foreach (HotkeyCombination combination in hotkey.Combinations)
        {
            // A key with no Ctrl, Alt, Shift or Win is taken away from every other program on the
            // machine for as long as the shortcut exists. Allowed, but said out loud.
            if ((combination.Modifiers & ~HotkeyModifiers.NoRepeat) == HotkeyModifiers.None)
            {
                warnings.Add(ValidationIssue.HotkeyNeedsModifier);
            }

            // The shell keeps a great many Win+something combinations for itself, and Windows
            // simply refuses to register those. The shortcut can be saved either way; what the
            // user should know is that if this is one of them it will show up as not registered
            // rather than silently doing nothing.
            if (combination.Modifiers.HasFlag(HotkeyModifiers.Windows))
            {
                warnings.Add(ValidationIssue.HotkeyWindowsKeyOftenReserved);
            }
        }

        return warnings.Distinct().ToList();
    }

    /// <summary>
    /// The shortcut already using this combination, or null when it is free.
    /// </summary>
    /// <remarks>
    /// Returns the offender itself rather than a yes or no, so the interface can say which action
    /// is in the way and offer to take the combination from it.
    /// </remarks>
    public static HotkeyDefinition? FindConflict(
        HotkeyDefinition hotkey,
        IEnumerable<HotkeyDefinition>? others)
    {
        ArgumentNullException.ThrowIfNull(hotkey);

        if (hotkey.Key == HotkeyKey.None)
        {
            return null;
        }

        foreach (HotkeyDefinition other in others ?? Enumerable.Empty<HotkeyDefinition>())
        {
            if (!ReferenceEquals(other, hotkey) && SameCombination(other, hotkey))
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>
    /// El atajo que ya reclama esta pulsación, mirando las dos combinaciones de cada uno.
    /// </summary>
    /// <remarks>
    /// Por combinación suelta y no por atajo entero porque el editor tiene dos casillas, y decir
    /// "hay un conflicto" sin decir en cuál de las dos deja al usuario buscándolo.
    /// </remarks>
    public static HotkeyDefinition? FindConflict(
        HotkeyCombination combination,
        IEnumerable<HotkeyDefinition>? others)
    {
        if (!combination.IsSet)
        {
            return null;
        }

        return (others ?? Enumerable.Empty<HotkeyDefinition>())
            .FirstOrDefault(other => other.Combinations.Any(combination.SameAs));
    }

    /// <summary>
    /// Whether two shortcuts would ask Windows for the same keystroke.
    /// </summary>
    /// <remarks>
    /// NoRepeat is masked off on both sides: it changes how the shortcut behaves while held down,
    /// not which keystroke it claims, so two shortcuts that differ only in that still collide.
    /// </remarks>
    private static bool SameCombination(HotkeyDefinition left, HotkeyDefinition right) =>
        left.Combinations.Any(one => right.Combinations.Any(one.SameAs));

    /// <summary>
    /// Lo que impide guardar una regla: sin nombre, sin criterios, sin acciones o con una expresión
    /// regular de título que no compila. Lista vacía si se puede guardar.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(WindowRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        List<ValidationIssue> issues = new();

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            issues.Add(ValidationIssue.RuleNeedsName);
        }

        if (!rule.Match.HasAnyCriteria)
        {
            issues.Add(ValidationIssue.RuleNeedsCriteria);
        }

        if (!rule.Actions.HasAny)
        {
            issues.Add(ValidationIssue.RuleNeedsActions);
        }

        if (!string.IsNullOrWhiteSpace(rule.Match.TitleRegex) && !IsValidRegex(rule.Match.TitleRegex))
        {
            issues.Add(ValidationIssue.RuleInvalidRegex);
        }

        return issues;
    }

    /// <summary>Si .NET acepta el patrón como expresión regular. No la ejecuta: sólo la compila.</summary>
    public static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Identifier suggested for a new shortcut, unique within the ones that exist.</summary>
    public static string SuggestHotkeyId(CommandType command, IEnumerable<HotkeyDefinition> existing)
    {
        string baseName = string.Concat(command.ToString().Select(c => char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()))
            .Trim('-');

        HashSet<string> taken = new(existing.Select(h => h.Id), StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidate = $"{baseName}-{suffix}";

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N")[..8];
    }
}
