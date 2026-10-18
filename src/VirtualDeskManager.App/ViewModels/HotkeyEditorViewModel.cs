using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using VirtualDeskManager.App.Localization;
using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>One selectable action in the shortcut editor.</summary>
public sealed class CommandChoice
{
    /// <summary>La opción de una acción, con su nombre ya traducido.</summary>
    public CommandChoice(CommandType type)
    {
        Type = type;
        Display = UiText.Command(type);
    }

    /// <summary>La acción.</summary>
    public CommandType Type { get; }

    /// <summary>Lo que se lee en el desplegable «Acción».</summary>
    public string Display { get; }

    /// <summary>Only these two need to be told which desktop they act on.</summary>
    public bool NeedsDesktopNumber =>
        Type is CommandType.SwitchToDesktop or CommandType.MoveWindowToDesktop;
}

/// <summary>Cuál de las dos casillas de captura está recibiendo las teclas.</summary>
public enum CaptureSlot
{
    /// <summary>La combinación principal, la que todo atajo tiene que tener.</summary>
    Primary,
    /// <summary>La «Segunda combinación (opcional)».</summary>
    Alternate
}

/// <summary>Editor of a single shortcut (spec section 30).</summary>
public sealed class HotkeyEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<HotkeyDefinition> _others;

    private string _id;
    private HotkeyCombination _primary;
    private HotkeyCombination _alternate;
    private bool _enabled;
    private CommandChoice _command;
    private int _desktopNumber = 1;
    private string _error = "";

    /// <summary>Prepara el editor para un atajo nuevo o para uno que ya existe.</summary>
    /// <param name="existing">El atajo que se edita; <c>null</c> para crear uno nuevo, que parte de Ctrl+Alt.</param>
    /// <param name="all">Todos los atajos, para avisar de choques y proponer un identificador libre.</param>
    public HotkeyEditorViewModel(HotkeyDefinition? existing, IReadOnlyList<HotkeyDefinition> all)
    {
        IsNew = existing is null;
        _others = all.Where(h => !ReferenceEquals(h, existing)).ToList();

        foreach (CommandType type in System.Enum.GetValues<CommandType>())
        {
            if (type != CommandType.None)
            {
                Commands.Add(new CommandChoice(type));
            }
        }

        HotkeyDefinition source = existing ?? new HotkeyDefinition
        {
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Command = new CommandDefinition(CommandType.ToggleAlwaysOnTop)
        };

        _id = source.Id;
        _primary = source.Primary;
        _alternate = source.Alternate;
        _enabled = source.Enabled;
        _command = Commands.First(c => c.Type == source.Command.Type);

        if (source.Command.TryGetInt("number", out int number))
        {
            _desktopNumber = number;
        }

        if (IsNew)
        {
            _id = ConfigurationValidator.SuggestHotkeyId(_command.Type, _others);
        }

        // So that opening an existing shortcut already shows whatever there is to say about it,
        // rather than waiting for the user to press something first.
        Refresh();
    }

    /// <summary>Se está creando un atajo, no editando uno que ya existía.</summary>
    public bool IsNew { get; }

    /// <summary>El título de la ventana: «Nuevo atajo» o «Editar atajo».</summary>
    public string Title => Loc.Get(IsNew ? "HotkeyEditor.TitleNew" : "HotkeyEditor.TitleEdit");

    /// <summary>Las acciones que ofrece el desplegable «Acción»: todas menos la de no hacer nada.</summary>
    public ObservableCollection<CommandChoice> Commands { get; } = new();

    /// <summary>
    /// El cuadro «Identificador»: el nombre del atajo en hotkeys.json. En uno nuevo se propone
    /// solo y cambia con la acción.
    /// </summary>
    public string Id
    {
        get => _id;
        set => Set(ref _id, value);
    }

    /// <summary>La casilla «Activa»: un atajo apagado se guarda, pero no se registra.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    /// <summary>
    /// El desplegable «Acción». En un atajo nuevo, al cambiarla se propone otro identificador
    /// acorde.
    /// </summary>
    public CommandChoice Command
    {
        get => _command;
        set
        {
            if (!Set(ref _command, value))
            {
                return;
            }

            Raise(nameof(NeedsDesktopNumber));

            // A shortcut created a moment ago still carries the suggested name of the old action.
            if (IsNew)
            {
                Id = ConfigurationValidator.SuggestHotkeyId(value.Type, _others);
            }
        }
    }

    /// <summary>La acción elegida necesita un escritorio: se enseña el cuadro «Número de escritorio».</summary>
    public bool NeedsDesktopNumber => _command.NeedsDesktopNumber;

    /// <summary>El cuadro «Número de escritorio», empezando en 1.</summary>
    public int DesktopNumber
    {
        get => _desktopNumber;
        set => Set(ref _desktopNumber, value);
    }

    /// <summary>Combination as the user reads it, e.g. <c>Ctrl + Alt + T</c>.</summary>
    public string Combination => _primary.IsSet ? _primary.DisplayText : Loc.Get("HotkeyEditor.Press");

    /// <summary>La segunda, con su propio texto cuando está vacía: no es lo mismo que faltar.</summary>
    public string AlternateCombination =>
        _alternate.IsSet ? _alternate.DisplayText : Loc.Get("HotkeyEditor.NoSecond");

    /// <summary>
    /// La segunda casilla no se toca hasta que hay una primera.
    /// </summary>
    /// <remarks>
    /// Un atajo con segunda combinación y sin primera no tiene sentido, y dejar capturar ahí
    /// primero obligaría a explicarle después al usuario por qué lo que acaba de pulsar se ha
    /// convertido en la combinación principal.
    /// </remarks>
    public bool CanSetAlternate => _primary.IsSet;

    /// <summary>Hay segunda combinación: se enseña el botón «Quitar».</summary>
    public bool HasAlternate => _alternate.IsSet;

    /// <summary>Por qué no se puede guardar todavía, en rojo bajo el formulario; vacío si no hay nada.</summary>
    public string Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>Los atajos que ya usan alguna de estas dos combinaciones.</summary>
    public HotkeyDefinition? PrimaryConflict { get; private set; }

    /// <summary>El atajo que ya usa la segunda combinación, si alguno la usa.</summary>
    public HotkeyDefinition? AlternateConflict { get; private set; }

    /// <summary>
    /// Alguna de las dos combinaciones ya la tiene otro atajo: se enseña el aviso con el botón
    /// «Usársela de todos modos».
    /// </summary>
    public bool HasConflict => PrimaryConflict is not null || AlternateConflict is not null;

    /// <summary>
    /// Qué acción está en medio y en cuál de las dos casillas, para que el usuario decida sabiendo
    /// a qué se la estaría quitando.
    /// </summary>
    public string ConflictMessage
    {
        get
        {
            List<string> lines = new();

            if (PrimaryConflict is not null)
            {
                lines.Add(Describe(_primary, PrimaryConflict));
            }

            // Una línea por casilla aunque las dos choquen con el mismo atajo: lo que el usuario
            // necesita saber es cuál de las suyas está ocupada, no cuántos vecinos hay.
            if (AlternateConflict is not null)
            {
                lines.Add(Describe(_alternate, AlternateConflict));
            }

            return string.Join("\n", lines);
        }
    }

    private static string Describe(HotkeyCombination combination, HotkeyDefinition owner) =>
        Loc.Format("HotkeyEditor.Conflict", combination.DisplayText, UiText.Command(owner.Command.Type), owner.Id);

    /// <summary>Set once the user has said yes to taking the combination.</summary>
    public bool OverwriteConfirmed { get; private set; }

    /// <summary>
    /// Los atajos que hay que apagar porque éste se ha quedado con su combinación, una vez que el
    /// usuario ha dicho que sí.
    /// </summary>
    /// <remarks>
    /// Una lista y no uno solo desde que hay dos combinaciones: cada una puede estar chocando con
    /// un atajo distinto, y apagar sólo el primero dejaría el segundo sin la suya y encendido.
    /// </remarks>
    public IReadOnlyList<HotkeyDefinition> Displaced { get; private set; } = System.Array.Empty<HotkeyDefinition>();

    /// <summary>Something worth saying about the combination that is not a reason to refuse it.</summary>
    public string Warning { get; private set; } = "";

    /// <summary>Hay algo que advertir: se enseña el recuadro con <see cref="Warning"/>.</summary>
    public bool HasWarning => Warning.Length > 0;

    /// <summary>Takes what the user just pressed. Modifiers alone are ignored.</summary>
    public void Capture(HotkeyModifiers modifiers, HotkeyKey key, CaptureSlot slot = CaptureSlot.Primary)
    {
        if (key == HotkeyKey.None)
        {
            return;
        }

        if (slot == CaptureSlot.Alternate)
        {
            if (!CanSetAlternate)
            {
                return;
            }

            _alternate = new HotkeyCombination(modifiers, key);
        }
        else
        {
            _primary = new HotkeyCombination(modifiers, key);
        }

        // A fresh combination cancels any permission given for the previous one.
        OverwriteConfirmed = false;
        Displaced = System.Array.Empty<HotkeyDefinition>();

        Refresh();
    }

    /// <summary>Quita la segunda combinación y deja el atajo con una sola.</summary>
    public void ClearAlternate()
    {
        _alternate = HotkeyCombination.None;
        OverwriteConfirmed = false;
        Displaced = System.Array.Empty<HotkeyDefinition>();

        Refresh();
    }

    /// <summary>Takes the combination from whatever had it.</summary>
    public void ConfirmOverwrite()
    {
        if (!HasConflict)
        {
            return;
        }

        OverwriteConfirmed = true;
        Error = "";
        Raise(nameof(CanSave));
    }

    /// <summary>False only while a conflict is waiting to be answered.</summary>
    public bool CanSave => !HasConflict || OverwriteConfirmed;

    private void Refresh()
    {
        HotkeyDefinition candidate = Build();

        PrimaryConflict = ConfigurationValidator.FindConflict(_primary, _others);
        AlternateConflict = ConfigurationValidator.FindConflict(_alternate, _others);

        Warning = string.Join(
            " ",
            ConfigurationValidator.Warnings(candidate).Select(w => Loc.Get("Validation." + w)));

        Error = "";

        Raise(nameof(Combination));
        Raise(nameof(AlternateCombination));
        Raise(nameof(CanSetAlternate));
        Raise(nameof(HasAlternate));
        Raise(nameof(PrimaryConflict));
        Raise(nameof(AlternateConflict));
        Raise(nameof(HasConflict));
        Raise(nameof(ConflictMessage));
        Raise(nameof(Warning));
        Raise(nameof(HasWarning));
        Raise(nameof(CanSave));
    }

    private HotkeyDefinition Build()
    {
        HotkeyDefinition hotkey = new()
        {
            Id = Id.Trim(),
            Modifiers = _primary.Modifiers,
            Key = _primary.Key,
            AlternateModifiers = _alternate.Modifiers,
            AlternateKey = _alternate.Key,
            Enabled = Enabled,
            Command = new CommandDefinition(_command.Type)
        };

        if (_command.NeedsDesktopNumber)
        {
            hotkey.Command.Parameters["number"] = DesktopNumber.ToString(CultureInfo.InvariantCulture);
        }

        return hotkey;
    }

    /// <summary>Returns the edited shortcut, or null when something is still wrong.</summary>
    public HotkeyDefinition? TryBuild()
    {
        HotkeyDefinition hotkey = Build();

        IReadOnlyList<ValidationIssue> issues = ConfigurationValidator.Validate(hotkey, _others);

        // The clash of combinations is handled on its own: it is the one problem the user can
        // settle from here, by taking the combination, rather than having to go and change the
        // other shortcut first.
        if (HasConflict && OverwriteConfirmed)
        {
            issues = issues.Where(i => i != ValidationIssue.HotkeyDuplicateCombination).ToList();
        }

        if (issues.Count > 0)
        {
            Error = string.Join(" ", issues.Select(i => Loc.Get("Validation." + i)));
            return null;
        }

        Displaced = OverwriteConfirmed
            ? new[] { PrimaryConflict, AlternateConflict }
                .OfType<HotkeyDefinition>()
                .Distinct()
                .ToList()
            : System.Array.Empty<HotkeyDefinition>();

        Error = "";
        return hotkey;
    }
}
