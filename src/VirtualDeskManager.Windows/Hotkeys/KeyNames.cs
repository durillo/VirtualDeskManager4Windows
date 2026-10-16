using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Hotkeys;

/// <summary>
/// Names keys the way they are printed on the keyboard in front of the user.
/// </summary>
/// <remarks>
/// Which character a key produces is a property of the keyboard layout, not of the key. The same
/// key that Windows calls <c>Oem5</c> is <c>º ª \</c> on a Spanish keyboard and something else
/// elsewhere, and a shortcut editor that answers "Oem5" when the user pressed the key with º
/// printed on it is telling them nothing they can act on.
///
/// Only the keys whose enum name is unhelpful are translated. Arrows, function keys and the like
/// already read perfectly well, and asking the layout about them gives nothing back anyway.
/// </remarks>
public static class KeyNames
{
    /// <summary>
    /// The character the key produces on the current layout, or null to use the enum name.
    /// </summary>
    public static string? Resolve(HotkeyKey key)
    {
        if (!NeedsLayout(key))
        {
            return null;
        }

        try
        {
            uint character = User32.MapVirtualKey((uint)key, User32.MAPVK_VK_TO_CHAR) & 0x7FFF;

            // Anything at or below space is a control character with nothing to print.
            return character > 32 ? ((char)character).ToString() : null;
        }
        catch (Exception)
        {
            // A missing export must not take the shortcut editor down with it.
            return null;
        }
    }

    /// <summary>
    /// True for the keys whose name depends on the layout: the OEM range and the two stray ones
    /// either side of it.
    /// </summary>
    private static bool NeedsLayout(HotkeyKey key)
    {
        int code = (int)key;

        return code is >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;
    }
}
