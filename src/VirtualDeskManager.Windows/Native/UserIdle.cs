using System.Runtime.InteropServices;

namespace VirtualDeskManager.Windows.Native;

/// <summary>Cuánto lleva el usuario sin tocar el teclado ni el ratón en esta sesión.</summary>
public static class UserIdle
{
    /// <summary>
    /// El tiempo desde la última entrada. Cero si Windows no lo dice: ante la duda, se da por que
    /// el usuario está delante.
    /// </summary>
    public static TimeSpan Elapsed()
    {
        User32.LASTINPUTINFO info = new() { cbSize = (uint)Marshal.SizeOf<User32.LASTINPUTINFO>() };

        if (!User32.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Los dos son de 32 bits y dan la vuelta a los 49 días: la resta sin signo lo aguanta.
        uint ahora = (uint)Environment.TickCount;
        return TimeSpan.FromMilliseconds(unchecked(ahora - info.dwTime));
    }
}
