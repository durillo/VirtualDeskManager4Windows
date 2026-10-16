using Microsoft.Win32;

namespace VirtualDeskManager.Windows.VirtualDesktops.Internal;

/// <summary>
/// Reads what Windows stores about the virtual desktops in the registry.
/// </summary>
/// <remarks>
/// Two uses. First, the names the user gave to each desktop, which the internal interfaces only
/// expose through a method whose position in the table is not verified. Second, and more
/// importantly, an independent source of truth: how many desktops there are and which one is
/// active. The backend compares its own answers against these before declaring itself usable,
/// which is what turns "an undocumented interface changed" into a graceful fallback instead of
/// wrong behaviour (spec section 44).
/// </remarks>
internal static class DesktopNameRegistry
{
    private const string VirtualDesktopsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>Desktop identifiers in the order Task View shows them, or an empty list.</summary>
    public static IReadOnlyList<Guid> ReadDesktopIds()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKey);

            if (key?.GetValue("VirtualDesktopIDs") is not byte[] raw)
            {
                return Array.Empty<Guid>();
            }

            List<Guid> identifiers = new(raw.Length / 16);

            for (int offset = 0; offset + 16 <= raw.Length; offset += 16)
            {
                identifiers.Add(new Guid(raw.AsSpan(offset, 16)));
            }

            return identifiers;
        }
        catch (Exception)
        {
            return Array.Empty<Guid>();
        }
    }

    public static Guid? ReadCurrentDesktopId()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsKey);

            return key?.GetValue("CurrentVirtualDesktop") is byte[] raw && raw.Length >= 16
                ? new Guid(raw.AsSpan(0, 16))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Renames a desktop by writing where Windows itself keeps the name.
    /// </summary>
    /// <remarks>
    /// The internal interfaces do have a SetDesktopName, but it sits past SwitchDesktop in the
    /// stretch of the table whose order changes between builds and that this project refuses to
    /// guess (spec section 43). Writing the value directly was measured on build 26200: the name
    /// is still there seconds later, so Windows does not overwrite it.
    ///
    /// What is not verified is whether Task View repaints an already open view. The application
    /// reads this same value, so its own interface is right immediately either way.
    /// </remarks>
    public static bool WriteName(Guid desktopId, string name)
    {
        try
        {
            string path = $@"{VirtualDesktopsKey}\Desktops\{desktopId.ToString("B").ToUpperInvariant()}";

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("Name", name ?? string.Empty, RegistryValueKind.String);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Name the user gave to a desktop, or an empty string when it was never renamed.</summary>
    public static string ReadName(Guid desktopId)
    {
        try
        {
            string path = $@"{VirtualDesktopsKey}\Desktops\{desktopId.ToString("B").ToUpperInvariant()}";

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue("Name") as string ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
