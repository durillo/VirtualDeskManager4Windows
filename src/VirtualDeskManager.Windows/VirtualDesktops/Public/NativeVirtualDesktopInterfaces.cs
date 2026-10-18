using System.Runtime.InteropServices;

namespace VirtualDeskManager.Windows.VirtualDesktops.Public;

/// <summary>
/// The documented virtual desktop interface (spec section 55). It only offers three operations,
/// which is precisely why enumeration, direct switching and pinning are modelled as separate
/// capabilities served by a different backend.
/// </summary>
[ComImport]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out int onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
}

internal static class VirtualDesktopClsids
{
    /// <summary>CLSID_VirtualDesktopManager.</summary>
    public static readonly Guid VirtualDesktopManager = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
}
