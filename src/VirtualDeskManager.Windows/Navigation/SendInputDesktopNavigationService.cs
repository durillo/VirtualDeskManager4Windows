using System.Runtime.InteropServices;
using VirtualDeskManager.Core.Abstractions;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Navigation;

/// <summary>
/// Left / right desktop navigation (spec section 15).
/// </summary>
/// <remarks>
/// The public virtual desktop API cannot switch desktops, so this implementation synthesizes the
/// shell shortcut with SendInput. It is a compatibility mechanism, entirely hidden behind
/// <see cref="IDesktopNavigationService"/>, and it is replaced by a real call once the advanced
/// backend of milestone 3 exists.
///
/// Alt and Shift are released first: when the command comes from Ctrl+Alt+Arrow those keys are
/// still physically down, and Windows would not recognise the shell combination otherwise.
/// </remarks>
public sealed class SendInputDesktopNavigationService : IDesktopNavigationService
{
    private readonly IAppLogger _logger;

    /// <summary>Creates the service, switched on or off by the settings.</summary>
    /// <param name="enabled">When false, every command answers <see cref="OperationStatus.Unsupported"/> and no key is sent.</param>
    /// <param name="logger">Optional diagnostic log.</param>
    public SendInputDesktopNavigationService(bool enabled, IAppLogger? logger = null)
    {
        IsSupported = enabled;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>Whether navigation by keystroke is enabled in the settings.</summary>
    public bool IsSupported { get; }

    /// <summary>Sends Win+Ctrl+Left to the shell.</summary>
    public Task<OperationResult> SwitchLeftAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Switch(NativeConstants.VK_LEFT, "left"));

    /// <summary>Sends Win+Ctrl+Right to the shell.</summary>
    public Task<OperationResult> SwitchRightAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Switch(NativeConstants.VK_RIGHT, "right"));

    private OperationResult Switch(ushort arrowKey, string direction)
    {
        if (!IsSupported)
        {
            return OperationResult.NotSupported("Desktop navigation is disabled in settings.");
        }

        List<INPUT> inputs = new(10);

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
        inputs.Add(Key(arrowKey, down: true, extended: true));
        inputs.Add(Key(arrowKey, down: false, extended: true));
        inputs.Add(Key(NativeConstants.VK_CONTROL, down: false));
        inputs.Add(Key(NativeConstants.VK_LWIN, down: false, extended: true));

        INPUT[] buffer = inputs.ToArray();
        uint sent = User32.SendInput((uint)buffer.Length, buffer, Marshal.SizeOf<INPUT>());

        if (sent != buffer.Length)
        {
            int error = Marshal.GetLastWin32Error();

            _logger.Warn(
                LogEvents.DesktopOperationFailed,
                ("Operation", "SwitchDesktop"),
                ("Direction", direction),
                ("Sent", sent),
                ("Expected", buffer.Length),
                ("Error", error));

            return error == NativeConstants.ERROR_ACCESS_DENIED
                ? OperationResult.Denied("Input was blocked, usually by a window running elevated.")
                : OperationResult.Fail($"SendInput failed (Win32 error {error}).");
        }

        _logger.Info(LogEvents.DesktopOperation, ("Operation", "SwitchDesktop"), ("Direction", direction));
        return OperationResult.Ok();
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
