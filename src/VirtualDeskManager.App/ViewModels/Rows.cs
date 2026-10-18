using System;
using VirtualDeskManager.App.Localization;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Localization;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.App.ViewModels;

/// <summary>One line of the capabilities panel of the dashboard (spec section 29).</summary>
public sealed class CapabilityRow
{
    /// <param name="key">The translation key of the capability's name.</param>
    /// <param name="available">Whether this machine has it.</param>
    public CapabilityRow(string key, bool available)
    {
        Name = Loc.Get(key);
        Available = available;
        Value = UiText.YesNo(available);
    }

    /// <summary>The capability's name, already translated.</summary>
    public string Name { get; }

    /// <summary>Whether this machine has it.</summary>
    public bool Available { get; }

    /// <summary>The translated yes or no shown next to the name.</summary>
    public string Value { get; }
}

/// <summary>One line of the Hotkeys screen (spec section 30).</summary>
public sealed class HotkeyRow
{
    /// <param name="definition">The shortcut as stored in the settings: combination, command and id.</param>
    /// <param name="registration">
    /// What happened when Windows was asked for the combination. Null when the shortcut is in the
    /// file but was never offered to Windows.
    /// </param>
    public HotkeyRow(HotkeyDefinition definition, HotkeyRegistration? registration)
    {
        Definition = definition;
        Shortcut = definition.DisplayText;
        Action = UiText.Command(definition.Command);
        Id = definition.Id;
        // Tres estados y no dos: un atajo atrapado por gancho funciona, pero deja de hacerlo con
        // una ventana de administrador delante, y el usuario tiene que poder saberlo de un vistazo.
        Status = Loc.Get(registration switch
        {
            { IsRegistered: true, ViaHook: true } => "Hotkeys.ViaHook",
            { IsRegistered: true } => "Hotkeys.Registered",
            _ => "Hotkeys.Conflict"
        });
        Error = registration?.Error ?? string.Empty;
        IsConflict = registration is not { IsRegistered: true };
        (GroupOrder, Group) = GroupOf(definition.Command.Type);
    }

    /// <summary>Que no funciona: Windows no se la dio y tampoco la atrapa el gancho.</summary>
    public bool IsConflict { get; }

    /// <summary>El grupo en el que sale en la lista (Escritorios, Ventanas…), ya traducido.</summary>
    public string Group { get; }

    /// <summary>El orden del grupo en la lista, para que salgan siempre igual.</summary>
    public int GroupOrder { get; }

    private static (int Order, string Name) GroupOf(CommandType command) => command switch
    {
        CommandType.SwitchDesktopLeft or CommandType.SwitchDesktopRight or CommandType.SwitchToDesktop
            or CommandType.ShowAllDesktops or CommandType.HideAllDesktops or CommandType.ShowDesktopGrid
            or CommandType.HideDesktopGrid or CommandType.ToggleDesktopGrid => (1, Loc.Get("Tab.Desktops")),
        CommandType.MoveWindowToZone => (3, Loc.Get("Tab.Zones")),
        CommandType.ToggleDropDownConsole or CommandType.ToggleDropDownConsolePin => (4, Loc.Get("Tab.Console")),
        _ => (2, Loc.Get("HotkeyGroup.Windows"))
    };

    /// <summary>The shortcut as it is in the file, handed to the editor when the row is edited or deleted.</summary>
    public HotkeyDefinition Definition { get; }

    /// <summary>The shortcut's identifier in hotkeys.json.</summary>
    public string Id { get; }

    /// <summary>The "Shortcut" column: the key combination as it reads.</summary>
    public string Shortcut { get; }

    /// <summary>The "Action" column: what the shortcut does, translated.</summary>
    public string Action { get; }

    /// <summary>The "Status" column: registered, caught by the hook, or in conflict.</summary>
    public string Status { get; }

    /// <summary>The "Detail" column: why Windows refused it; empty when all went well.</summary>
    public string Error { get; }
}

/// <summary>One line of the Rules screen; the check box writes straight back to rules.json (spec section 31).</summary>
public sealed class RuleRow : ObservableObject
{
    private readonly WindowRule _rule;
    private readonly Action<RuleRow> _onEnabledChanged;

    /// <param name="rule">The rule shown on this line.</param>
    /// <param name="onEnabledChanged">Called when the check box is ticked or cleared, to save the rules.</param>
    public RuleRow(WindowRule rule, Action<RuleRow> onEnabledChanged)
    {
        _rule = rule;
        _onEnabledChanged = onEnabledChanged;
        Match = UiText.DescribeMatch(rule.Match);
        Actions = UiText.DescribeActions(rule.Actions);
    }

    /// <summary>The rule itself, handed to the editor when the row is edited or deleted.</summary>
    public WindowRule Rule => _rule;

    /// <summary>The "Enabled" check box. Changing it is saved straight away.</summary>
    public bool Enabled
    {
        get => _rule.Enabled;
        set
        {
            if (_rule.Enabled == value)
            {
                return;
            }

            _rule.Enabled = value;
            Raise();
            _onEnabledChanged(this);
        }
    }

    /// <summary>The "Name" column.</summary>
    public string Name => _rule.Name;

    /// <summary>The "Priority" column.</summary>
    public int Priority => _rule.Priority;

    /// <summary>The "Condition" column: which windows the rule applies to, in words.</summary>
    public string Match { get; }

    /// <summary>The "Actions" column: what the rule does to them, in words.</summary>
    public string Actions { get; }
}

/// <summary>A configuration problem, shown instead of being buried in the log (spec section 35).</summary>
public sealed class IssueRow
{
    /// <param name="file">The configuration file with the problem.</param>
    /// <param name="message">What is wrong with it.</param>
    public IssueRow(string file, string message)
    {
        File = file;
        Message = message;
    }

    /// <summary>The configuration file with the problem.</summary>
    public string File { get; }

    /// <summary>What is wrong with it.</summary>
    public string Message { get; }
}

/// <summary>
/// One virtual desktop in the list, with its name editable.
/// </summary>
/// <remarks>
/// <see cref="OriginalName"/> is kept so that saving only renames what the user actually
/// touched. Without it every desktop would be written on every save, and a name Windows had
/// given itself would be rewritten as if the user had chosen it.
/// </remarks>
public sealed class DesktopRow : ObservableObject
{
    private string _name;

    /// <param name="id">The desktop's identifier in Windows.</param>
    /// <param name="number">Its position, starting at 1.</param>
    /// <param name="name">The name it has now.</param>
    public DesktopRow(Guid id, int number, string name)
    {
        Id = id;
        Number = number;
        OriginalName = name ?? string.Empty;
        _name = OriginalName;
    }

    /// <summary>The desktop's identifier in Windows.</summary>
    public Guid Id { get; }

    /// <summary>Its position in the list, starting at 1.</summary>
    public int Number { get; }

    /// <summary>The name it had when the list was read.</summary>
    public string OriginalName { get; }

    /// <summary>The name as the user is editing it; it is only applied when the names are saved.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>Que es el escritorio en el que se está ahora.</summary>
    public bool IsCurrent { get; init; }
}

/// <summary>Un aviso de «Necesita tu atención» en Inicio, con la sección donde se arregla.</summary>
public sealed class AttentionRow
{
    /// <param name="text">Qué pasa, ya traducido.</param>
    /// <param name="section">La clave de la sección donde se arregla, como «hotkeys».</param>
    public AttentionRow(string text, string section)
    {
        Text = text;
        Section = section;
    }

    /// <summary>Qué pasa, ya traducido.</summary>
    public string Text { get; }

    /// <summary>La clave de la sección donde se arregla.</summary>
    public string Section { get; }
}
