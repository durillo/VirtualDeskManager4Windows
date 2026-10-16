using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Core.Rules;

namespace VirtualDeskManager.Core.Configuration;

/// <summary>Files written on first run (spec sections 25 to 27).</summary>
public static class DefaultConfiguration
{
    /// <summary>Ajustes de primera vez: los valores por defecto de <see cref="AppSettings"/>, sin más.</summary>
    public static AppSettings CreateSettings() => new();

    /// <summary>Identifier of the shortcut that jumps straight to desktop <paramref name="number"/>.</summary>
    public static string NumberedDesktopId(int number) => $"desktop-{number}";

    /// <summary>
    /// Modifiers of the numbered desktop shortcuts.
    /// </summary>
    /// <remarks>
    /// Not Ctrl+Alt, which is what these shipped with and had to be changed: <b>AltGr is
    /// Ctrl+Alt</b>. On a Spanish keyboard AltGr+1 is <c>|</c>, AltGr+2 is <c>@</c>, AltGr+3 is
    /// <c>#</c> and AltGr+4 is <c>~</c>, so registering Ctrl+Alt+1..9 globally took those
    /// characters away from every application on the machine, and typing a pipe in an editor
    /// jumped the user to another desktop. The same goes for French, German, Italian, Portuguese
    /// and most European layouts.
    ///
    /// Not Ctrl+Win either, which was the first attempt: measured on build 26200, all nine are
    /// already taken. Windows uses Win+Ctrl+number to cycle the windows of the Nth taskbar
    /// application. So are Win+Alt+number, Win+Shift+number and Ctrl+Shift+Win+number.
    ///
    /// Ctrl+Shift is what is left standing: nine out of nine free when measured, no AltGr, and no
    /// Windows key, which section 55 asks to keep out of the defaults. It is not free of cost —
    /// some applications use Ctrl+Shift+number internally, Excel among them, and a global
    /// shortcut wins over an application one — but that is a shortcut the user can move, not a
    /// broken keyboard.
    /// </remarks>
    public static HotkeyModifiers NumberedDesktopModifiers =>
        HotkeyModifiers.Control | HotkeyModifiers.Shift;

    /// <summary>
    /// Ctrl+Win+1 to Ctrl+Win+9: straight to that desktop, without walking through the ones in
    /// between (spec section 15).
    /// </summary>
    /// <remarks>
    /// Nine of them regardless of how many desktops exist. A shortcut pointing at a desktop that
    /// is not there answers that it does not exist and does nothing, which is better than the
    /// shortcuts changing under the user every time a desktop is added or removed.
    /// </remarks>
    public static IEnumerable<HotkeyDefinition> CreateNumberedDesktopHotkeys()
    {
        for (int number = 1; number <= 9; number++)
        {
            CommandDefinition command = new(CommandType.SwitchToDesktop);
            command.Parameters["number"] = number.ToString();

            yield return new HotkeyDefinition
            {
                Id = NumberedDesktopId(number),
                Modifiers = NumberedDesktopModifiers,
                Key = (HotkeyKey)((int)HotkeyKey.D1 + number - 1),
                Command = command
            };
        }
    }

    /// <summary>
    /// Win+Ctrl+Arriba abre la vista de todos los escritorios y Win+Ctrl+Abajo la cierra.
    /// </summary>
    /// <remarks>
    /// Arriba para verlo todo y abajo para volver, que es la misma idea que ya usa Windows con
    /// Win+Ctrl+Izquierda y Win+Ctrl+Derecha para moverse entre escritorios. Medidas las dos
    /// libres en la compilación 26200; las de izquierda y derecha, de la misma familia, están
    /// cogidas por el propio Windows.
    ///
    /// Dos atajos y no uno que alterne: alternar obliga a acordarse de si la vista está abierta,
    /// y el usuario pidió expresamente uno para abrir y otro para quitar.
    /// </remarks>
    public static IEnumerable<HotkeyDefinition> CreateTaskViewHotkeys()
    {
        yield return new HotkeyDefinition
        {
            Id = ShowAllDesktopsId,
            Modifiers = HotkeyModifiers.Windows | HotkeyModifiers.Control,
            Key = HotkeyKey.Up,
            Command = new CommandDefinition(CommandType.ShowAllDesktops)
        };

        yield return new HotkeyDefinition
        {
            Id = HideAllDesktopsId,
            Modifiers = HotkeyModifiers.Windows | HotkeyModifiers.Control,
            Key = HotkeyKey.Down,
            Command = new CommandDefinition(CommandType.HideAllDesktops)
        };
    }

    /// <summary>Identificador del atajo que abre la vista de tareas de Windows.</summary>
    public const string ShowAllDesktopsId = "show-all-desktops";

    /// <summary>Identificador del atajo que cierra la vista de tareas de Windows.</summary>
    public const string HideAllDesktopsId = "hide-all-desktops";

    /// <summary>
    /// Ctrl+Alt+Arriba abre y cierra la vista propia de escritorios.
    /// </summary>
    /// <remarks>
    /// Un solo atajo que alterna, al contrario que la vista de Windows de arriba: aquella se
    /// cierra sola al elegir un escritorio, mientras que ésta es nuestra y la misma tecla que la
    /// abre es la forma natural de quitarla de en medio. Escape también la cierra.
    ///
    /// Ctrl+Alt+Arriba y no Win+Ctrl+Arriba porque ésa ya la gasta la vista de Windows, y la
    /// familia Ctrl+Alt+flecha es la que este programa usa para todo lo de escritorios.
    /// </remarks>
    public static IEnumerable<HotkeyDefinition> CreateDesktopGridHotkeys()
    {
        yield return new HotkeyDefinition
        {
            Id = ToggleDesktopGridId,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Key = HotkeyKey.Up,
            Command = new CommandDefinition(CommandType.ToggleDesktopGrid)
        };
    }

    /// <summary>Identificador del atajo que abre y cierra la vista propia de escritorios.</summary>
    public const string ToggleDesktopGridId = "toggle-desktop-grid";

    /// <summary>
    /// Ctrl+Shift+º despliega la consola, la tecla que en Quake era la de debajo de Escape.
    /// </summary>
    /// <remarks>
    /// <c>Oem5</c> y no <c>OemTilde</c>, que es lo que se puso primero y estaba mal. Medido el
    /// 2026-09-15 en este equipo con distribución española: la tecla de debajo de Escape, la de
    /// º ª \, es el código 0xDC (<c>Oem5</c>), mientras que 0xC0 (<c>OemTilde</c>) es la Ñ. El
    /// nombre despista porque viene del teclado estadounidense, donde esa posición da la ~.
    ///
    /// El modificador es Ctrl+Shift y no Ctrl+Alt por lo de siempre: Ctrl+Alt es AltGr, y AltGr+º
    /// es precisamente la barra invertida, así que ese atajo dejaría al usuario sin poder
    /// escribir \ en todo el equipo.
    ///
    /// Windows sola no se usa aunque sea lo más parecido a Quake: la reserva el shell.
    /// </remarks>
    public static IEnumerable<HotkeyDefinition> CreateDropDownConsoleHotkeys()
    {
        yield return new HotkeyDefinition
        {
            Id = ToggleDropDownConsoleId,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
            Key = HotkeyKey.Oem5,
            Command = new CommandDefinition(CommandType.ToggleDropDownConsole)
        };
    }

    /// <summary>Identificador del atajo que despliega y recoge la consola.</summary>
    public const string ToggleDropDownConsoleId = "toggle-drop-down-console";

    /// <summary>
    /// Ctrl+Alt+Mayús+º fija la consola para que no se recoja sola, o la suelta.
    /// </summary>
    /// <remarks>
    /// La misma tecla que la despliega, con Alt de más, para que se recuerde sin pensar.
    /// Ctrl+Alt+Mayús es AltGr+Mayús, pero AltGr+Mayús+º no escribe nada en el teclado español,
    /// así que no se le quita ningún carácter a nadie.
    /// </remarks>
    public static IEnumerable<HotkeyDefinition> CreateDropDownConsolePinHotkeys()
    {
        yield return new HotkeyDefinition
        {
            Id = ToggleDropDownConsolePinId,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            Key = HotkeyKey.Oem5,
            Command = new CommandDefinition(CommandType.ToggleDropDownConsolePin)
        };
    }

    /// <summary>Identificador del atajo que fija y suelta la consola.</summary>
    public const string ToggleDropDownConsolePinId = "toggle-drop-down-console-pin";

    /// <summary>
    /// Todos los atajos de serie: navegación y movimiento entre escritorios, siempre encima,
    /// fijar ventana, escritorios numerados, vista de tareas, vista propia y consola.
    /// </summary>
    /// <remarks>
    /// Sólo se escriben si hotkeys.json no existe. A quien ya lo tiene, los que llegaron más tarde
    /// se le añaden una vez, con las marcas <c>...HotkeysAdded</c> de <see cref="AppSettings"/>.
    /// </remarks>
    public static List<HotkeyDefinition> CreateHotkeys()
    {
        List<HotkeyDefinition> hotkeys = CreateBaseHotkeys();
        hotkeys.AddRange(CreateNumberedDesktopHotkeys());
        hotkeys.AddRange(CreateTaskViewHotkeys());
        hotkeys.AddRange(CreateDesktopGridHotkeys());
        hotkeys.AddRange(CreateDropDownConsoleHotkeys());
        hotkeys.AddRange(CreateDropDownConsolePinHotkeys());
        return hotkeys;
    }

    private static List<HotkeyDefinition> CreateBaseHotkeys() => new()
    {
        new HotkeyDefinition
        {
            Id = "desktop-left",
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Key = HotkeyKey.Left,
            Command = new CommandDefinition(CommandType.SwitchDesktopLeft)
        },
        new HotkeyDefinition
        {
            Id = "desktop-right",
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Key = HotkeyKey.Right,
            Command = new CommandDefinition(CommandType.SwitchDesktopRight)
        },
        new HotkeyDefinition
        {
            Id = "move-window-left",
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            Key = HotkeyKey.Left,
            Command = new CommandDefinition(CommandType.MoveWindowLeft)
        },
        new HotkeyDefinition
        {
            Id = "move-window-right",
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            Key = HotkeyKey.Right,
            Command = new CommandDefinition(CommandType.MoveWindowRight)
        },
        new HotkeyDefinition
        {
            Id = "toggle-topmost",
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Key = HotkeyKey.T,
            Command = new CommandDefinition(CommandType.ToggleAlwaysOnTop)
        },
        new HotkeyDefinition
        {
            Id = "toggle-pin",
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Key = HotkeyKey.P,
            Command = new CommandDefinition(CommandType.TogglePinWindow)
        }
    };

    /// <summary>
    /// Reglas de ejemplo para rules.json: Bloc de notas siempre encima (activada), y Calculadora
    /// siempre encima y Spotify en todos los escritorios (desactivadas).
    /// </summary>
    public static List<WindowRule> CreateRules() => new()
    {
        // Acceptance test of iteration 2 (spec section 50): opening Notepad makes it topmost.
        new WindowRule
        {
            Id = Guid.Parse("2f0c1f4e-7d64-4a1b-9c2f-2a6f5f8f0a11"),
            Name = "Notepad always on top",
            Enabled = true,
            Priority = 100,
            Match = new WindowMatcher { ProcessName = "notepad.exe" },
            Actions = new WindowActions { AlwaysOnTop = true }
        },
        new WindowRule
        {
            Id = Guid.Parse("9af29565-d12b-461a-a46f-739f886a1653"),
            Name = "Calculator always on top",
            Enabled = false,
            Priority = 100,
            Match = new WindowMatcher { ProcessName = "CalculatorApp.exe" },
            Actions = new WindowActions { AlwaysOnTop = true }
        },
        new WindowRule
        {
            Id = Guid.Parse("9ff0eb88-482a-49ae-856f-750c85ce984b"),
            Name = "Spotify everywhere",
            // Needs the advanced backend; disabled so it does not spam the log with Unsupported.
            Enabled = false,
            Priority = 100,
            Match = new WindowMatcher { ProcessName = "Spotify.exe" },
            Actions = new WindowActions { PinApplication = true }
        }
    };
}
