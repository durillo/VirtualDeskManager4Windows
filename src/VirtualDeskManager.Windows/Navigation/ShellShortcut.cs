using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Navigation;

/// <summary>
/// Synthesizes the shell's own keyboard shortcuts.
/// </summary>
/// <remarks>
/// Used for the virtual desktop operations that the interfaces do not expose safely. Creating a
/// desktop lives past <c>SwitchDesktop</c> in the internal table, in the stretch whose method
/// order changes between Windows builds and that this project does not verify (spec section 43).
/// Guessing a slot there means calling an unknown function with unknown arguments, and getting it
/// wrong takes Explorer down with it. Ctrl+Win+D is documented, does exactly the same thing, and
/// was measured to work on this build.
///
/// The price is honest and worth stating: it is synthetic input, so it needs a responsive shell,
/// it can be refused when a window running elevated has the foreground, and the result has to be
/// confirmed afterwards rather than trusted.
/// </remarks>
public static class ShellShortcut
{
    /// <summary>Presses Ctrl+Win+<paramref name="key"/>, releasing whatever would get in the way.</summary>
    public static OperationResult Send(ushort key)
    {
        List<INPUT> inputs = new(10);

        // The command usually arrives from a shortcut of ours whose keys are still physically
        // held down. Windows would not recognise the shell combination with those mixed in.
        if (IsPhysicallyDown(NativeConstants.VK_MENU))
        {
            inputs.Add(Key(NativeConstants.VK_MENU, down: false));
        }

        if (IsPhysicallyDown(NativeConstants.VK_SHIFT))
        {
            inputs.Add(Key(NativeConstants.VK_SHIFT, down: false));
        }

        inputs.Add(Key(NativeConstants.VK_LWIN, down: true, extended: true));
        inputs.Add(Key(NativeConstants.VK_CONTROL, down: true));
        inputs.Add(Key(key, down: true));
        inputs.Add(Key(key, down: false));
        inputs.Add(Key(NativeConstants.VK_CONTROL, down: false));
        inputs.Add(Key(NativeConstants.VK_LWIN, down: false, extended: true));

        INPUT[] buffer = inputs.ToArray();
        uint sent = User32.SendInput((uint)buffer.Length, buffer, Marshal.SizeOf<INPUT>());

        if (sent == buffer.Length)
        {
            return OperationResult.Ok();
        }

        int error = Marshal.GetLastWin32Error();

        return error == NativeConstants.ERROR_ACCESS_DENIED
            ? OperationResult.Denied("Input was blocked, usually by a window running elevated.")
            : OperationResult.Fail($"SendInput failed (Win32 error {error}).");
    }

    /// <summary>
    /// Presses Win+<paramref name="key"/>, without Ctrl.
    /// </summary>
    /// <remarks>
    /// The Windows key gets released before the combination is pressed, and that is the whole
    /// trick. This is normally called from a shortcut of ours that already uses Win, and once
    /// Windows has handed a Win combination to somebody it stops acting on further Win shortcuts
    /// until the key comes back up. Pressing Win again while it is still down changes nothing:
    /// measured, the shortcut fired, the input went out, and Task View never appeared.
    /// </remarks>
    public static OperationResult SendWin(ushort key)
    {
        List<INPUT> inputs = new(10);

        foreach (ushort modifier in new[] { NativeConstants.VK_MENU, NativeConstants.VK_CONTROL, NativeConstants.VK_SHIFT })
        {
            if (IsPhysicallyDown(modifier))
            {
                inputs.Add(Key(modifier, down: false));
            }
        }

        // Se espera a que el usuario suelte, en lugar de sintetizar la soltada y seguir de
        // inmediato. Windows deja de atender atajos de tecla Windows desde que entrega una
        // combinación hasta que la tecla vuelve a subir, y ese estado no se limpia en el mismo
        // suspiro: medido, inyectar sin esperar sale por SendInput sin error y no abre nada.
        EsperarATeclasSueltas();

        // No extended flag on the Windows key, unlike every other shortcut here. Measured on
        // build 26200: with it, Win+Tab goes out through SendInput without error and Task View
        // never appears; without it, it appears. The Ctrl+Win shortcuts do accept it, which is
        // why the other sender keeps it and this one does not.
        inputs.Add(Key(NativeConstants.VK_LWIN, down: true));
        inputs.Add(Key(key, down: true));
        inputs.Add(Key(key, down: false));
        inputs.Add(Key(NativeConstants.VK_LWIN, down: false));

        return Enviar(inputs.ToArray());
    }

    /// <summary>Presses one key on its own, with no modifiers.</summary>
    public static OperationResult SendPlain(ushort key) =>
        Enviar(new[] { Key(key, down: true), Key(key, down: false) });

    private static OperationResult Enviar(INPUT[] buffer)
    {
        uint sent = User32.SendInput((uint)buffer.Length, buffer, Marshal.SizeOf<INPUT>());

        if (sent == buffer.Length)
        {
            return OperationResult.Ok();
        }

        int error = Marshal.GetLastWin32Error();

        return error == NativeConstants.ERROR_ACCESS_DENIED
            ? OperationResult.Denied("Input was blocked, usually by a window running elevated.")
            : OperationResult.Fail($"SendInput failed (Win32 error {error}).");
    }

    /// <summary>
    /// Espera a que no quede ninguna tecla modificadora bajada, con un tope.
    /// </summary>
    /// <remarks>
    /// El tope importa tanto como la espera: si el usuario deja el dedo puesto, o si una tecla se
    /// queda pegada, más vale intentarlo igualmente que quedarse aquí colgado. Y la espera se
    /// hace a base de dormir poco y mirar, no de un retardo fijo: en el caso normal la tecla ya
    /// está arriba y no se pierde nada.
    /// </remarks>
    private static void EsperarATeclasSueltas(int topeMilisegundos = 600)
    {
        ushort[] modificadores =
        {
            NativeConstants.VK_MENU,
            NativeConstants.VK_CONTROL,
            NativeConstants.VK_SHIFT,
            NativeConstants.VK_LWIN,
            NativeConstants.VK_RWIN
        };

        for (int esperado = 0; esperado < topeMilisegundos; esperado += 20)
        {
            if (!modificadores.Any(IsPhysicallyDown))
            {
                // Un respiro después de la última soltada: el shell necesita procesarla antes de
                // volver a mirar la tecla Windows.
                Thread.Sleep(40);
                return;
            }

            Thread.Sleep(20);
        }
    }

    private static bool IsPhysicallyDown(ushort virtualKey) =>
        (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static INPUT Key(ushort virtualKey, bool down, bool extended = false)
    {
        uint flags = 0;

        if (extended)
        {
            flags |= NativeConstants.KEYEVENTF_EXTENDEDKEY;
        }

        if (!down)
        {
            flags |= NativeConstants.KEYEVENTF_KEYUP;
        }

        return new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = 0
            }
        };
    }
}
