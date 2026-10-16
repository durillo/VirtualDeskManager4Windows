using System.Runtime.InteropServices;

namespace VirtualDeskManager.Windows.VirtualDesktops.Internal;

/// <summary>
/// The undocumented shell interfaces used by Explorer and Task View.
/// </summary>
/// <remarks>
/// Everything in this file is <c>internal</c> on purpose: no GUID, no COM interface and no type
/// declared here may travel beyond <see cref="InternalVirtualDesktopService"/> (spec section 43).
///
/// Microsoft changes these interface identifiers between Windows builds, so nothing is assumed:
/// <see cref="InternalInterfaceIds.ManagerCandidates"/> is tried one by one at start up and, once
/// one answers, the method order is checked against data read from the registry before the
/// backend declares itself usable.
/// </remarks>
internal static class InternalClsids
{
    /// <summary>CLSID_ImmersiveShell, the service provider that hands out the shell services.</summary>
    public static readonly Guid ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");

    public static readonly Guid VirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    public static readonly Guid ApplicationViewCollection = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");

    public static readonly Guid VirtualDesktopPinnedApps = new("B5A399E7-1C87-46B8-88E9-FC5747B171BD");
}

internal static class InternalInterfaceIds
{
    /// <summary>
    /// Identifiers of IVirtualDesktopManagerInternal seen across Windows versions, newest first.
    /// Windows 11 build 26200 answers to the first two.
    /// </summary>
    public static readonly Guid[] ManagerCandidates =
    {
        new("53F5CA0B-158F-4124-900C-057158060B27"),
        new("4970BA3D-FD4E-4647-BEA3-D89076EF4B9C"),
        new("A3175F2D-239C-4BD2-8AA0-EEBA8B0B138E"),
        new("094AFE11-44F2-4BA0-976F-29A97E263EE0"),
        new("B2F925B9-5A0F-4D2E-9F4D-2B1507593C10"),
        new("F31574D6-B682-4CDC-BD56-1827860ABEC6"),
        new("AF8DA486-95BB-4460-B3B7-6E7A6B2962B5")
    };

    public static readonly Guid[] DesktopCandidates =
    {
        new("3F07F4BE-B107-441A-AF0F-39D82529072C"),
        new("536D3495-B208-4CC9-AE26-DE8111275BF8"),
        new("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4")
    };

    public static readonly Guid ApplicationViewCollection = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");

    public static readonly Guid VirtualDesktopPinnedApps = new("4CE81583-1E4C-4632-A621-07A53543148F");
}

[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid service, ref Guid riid, out nint instance);
}

[ComImport]
[Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object instance);
}

[ComImport]
[Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktop
{
    [PreserveSig]
    int IsViewVisible([MarshalAs(UnmanagedType.IUnknown)] object view, out int visible);

    [PreserveSig]
    int GetId(out Guid id);
}

/// <summary>
/// Only the first seven slots are declared, and they are the ones verified on this build. Slots
/// further down (creating and removing desktops, renaming, wallpaper) move around far more
/// between versions, so they stay out until they can be checked the same way.
/// </summary>
[ComImport]
[Guid("53F5CA0B-158F-4124-900C-057158060B27")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManagerInternal
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int MoveViewToDesktop(
        [MarshalAs(UnmanagedType.IUnknown)] object view,
        [MarshalAs(UnmanagedType.IUnknown)] object desktop);

    [PreserveSig]
    int CanViewMoveDesktops([MarshalAs(UnmanagedType.IUnknown)] object view, out int can);

    [PreserveSig]
    int GetCurrentDesktop(out IVirtualDesktop? desktop);

    [PreserveSig]
    int GetDesktops(out IObjectArray? desktops);

    [PreserveSig]
    int GetAdjacentDesktop(
        [MarshalAs(UnmanagedType.IUnknown)] object from,
        int direction,
        out IVirtualDesktop? found);

    [PreserveSig]
    int SwitchDesktop([MarshalAs(UnmanagedType.IUnknown)] object desktop);
}

[ComImport]
[Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationViewCollection
{
    [PreserveSig]
    int GetViews(out IObjectArray? array);

    [PreserveSig]
    int GetViewsByZOrder(out IObjectArray? array);

    [PreserveSig]
    int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IObjectArray? array);

    [PreserveSig]
    int GetViewForHwnd(nint hwnd, [MarshalAs(UnmanagedType.IUnknown)] out object? view);
}

[ComImport]
[Guid("4CE81583-1E4C-4632-A621-07A53543148F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopPinnedApps
{
    [PreserveSig]
    int IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string appId, out bool pinned);

    [PreserveSig]
    int PinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [PreserveSig]
    int UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [PreserveSig]
    int IsViewPinned([MarshalAs(UnmanagedType.IUnknown)] object view, out bool pinned);

    [PreserveSig]
    int PinView([MarshalAs(UnmanagedType.IUnknown)] object view);

    [PreserveSig]
    int UnpinView([MarshalAs(UnmanagedType.IUnknown)] object view);
}
