namespace VirtualDeskManager.Core.Configuration;

/// <summary>Which shell the drop down console runs.</summary>
/// <remarks>
/// An enumeration and not a free command line so that settings.json cannot turn into a way of
/// launching anything at all on a hotkey. <see cref="Custom"/> is the escape hatch, and it is
/// opt in: whoever writes it there has decided on purpose.
/// </remarks>
public enum ConsoleShell
{
    /// <summary>cmd.exe. The default, as asked.</summary>
    Cmd,

    /// <summary>powershell.exe, the one that ships with Windows.</summary>
    PowerShell,

    /// <summary>pwsh.exe, PowerShell 7, only when it is installed.</summary>
    PowerShellCore,

    /// <summary>wt.exe, Windows Terminal with its own default profile.</summary>
    WindowsTerminal,

    /// <summary>Whatever <see cref="DropDownConsoleSettings.CustomCommand"/> says.</summary>
    Custom
}

/// <summary>
/// The console that drops down from the top of the screen, in the manner of the Quake one
/// (spec section 17).
/// </summary>
/// <remarks>
/// The size is kept as a percentage of the screen and never in pixels: the same configuration
/// travels between a laptop and a 3440 wide monitor, and a height in pixels that looked right on
/// one of them is either a sliver or the whole screen on the other.
/// </remarks>
public sealed class DropDownConsoleSettings
{
    /// <summary>Whether the shortcut does anything at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which shell the console runs. <see cref="ConsoleShell.Cmd"/> by default.</summary>
    public ConsoleShell Shell { get; set; } = ConsoleShell.Cmd;

    /// <summary>Command line used when <see cref="Shell"/> is <see cref="ConsoleShell.Custom"/>.</summary>
    public string CustomCommand { get; set; } = "";

    /// <summary>Width of the console as a percentage of the screen, 10 to 100.</summary>
    public int WidthPercent { get; set; } = 80;

    /// <summary>Height of the console as a percentage of the screen, 10 to 100.</summary>
    public int HeightPercent { get; set; } = 45;

    /// <summary>How solid the console is, 20 to 100. 100 is opaque.</summary>
    public int OpacityPercent { get; set; } = 90;

    /// <summary>
    /// How long the slide takes, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Quake gave this a knob too (<c>scr_conspeed</c>), and for the same reason: the movement is
    /// what makes the console feel like an object rather than a box that blinks into existence,
    /// but a slow one is only charming until the fifth time you use it.
    /// </remarks>
    public int AnimationMilliseconds { get; set; } = 120;

    /// <summary>Roll the console back up as soon as it stops being the window in front.</summary>
    public bool HideWhenFocusLost { get; set; } = true;

    /// <summary>
    /// Bajar la consola en la pantalla donde esté el ratón, y no siempre en la principal.
    /// </summary>
    /// <remarks>
    /// Con una sola pantalla da exactamente igual, así que viene activado: quien tiene dos
    /// quiere la consola donde está mirando, y quien tiene una no nota nada.
    ///
    /// El ratón y no la ventana activa: quien pulsa el atajo suele tener el cursor en la pantalla
    /// a la que está atendiendo, aunque el foco de teclado se haya quedado en otra.
    /// </remarks>
    public bool FollowMouse { get; set; } = true;

    /// <summary>
    /// Keeps the shell alive while the console is hidden, so the next time it drops down the
    /// session, the history and whatever was running are still there.
    /// </summary>
    /// <remarks>
    /// This is the whole point of a console like this one, and the reason Quake's opened
    /// instantly: it is never started, only uncovered. Turning it off means a fresh shell and a
    /// visible wait on every keystroke.
    /// </remarks>
    public bool KeepShellRunning { get; set; } = true;

    /// <summary>Clamps every value to a range that can actually be drawn.</summary>
    /// <remarks>
    /// settings.json is a text file the user may edit by hand, so a zero, a negative or a
    /// thousand has to be survivable rather than a crash on the next keystroke.
    /// </remarks>
    public void Normalise()
    {
        WidthPercent = Math.Clamp(WidthPercent, 10, 100);
        HeightPercent = Math.Clamp(HeightPercent, 10, 100);
        OpacityPercent = Math.Clamp(OpacityPercent, 20, 100);
        AnimationMilliseconds = Math.Clamp(AnimationMilliseconds, 0, 2000);
    }

    /// <summary>Alpha for <c>SetLayeredWindowAttributes</c>, where 255 is opaque.</summary>
    /// <remarks>
    /// Integer arithmetic on purpose. Multiplying by 2.55 looks like the obvious way to do this
    /// and is wrong at the midpoint: 2.55 has no exact binary form, so 50% comes out as 127.4999
    /// and rounds down to 127 instead of 128. Scaling by 255 and adding half the divisor before
    /// dividing rounds half up exactly, with no floating point anywhere.
    /// </remarks>
    public byte Alpha => (byte)Math.Clamp((OpacityPercent * 255 + 50) / 100, 51, 255);
}
