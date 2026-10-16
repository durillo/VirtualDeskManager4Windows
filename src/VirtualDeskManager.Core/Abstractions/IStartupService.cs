namespace VirtualDeskManager.Core.Abstractions;

/// <summary>Start with Windows (spec section 33).</summary>
public interface IStartupService
{
    /// <summary>False when the option cannot work here, for instance when the executable path is unknown.</summary>
    bool IsSupported { get; }

    /// <summary>Whether Windows will launch the application at sign in. Reads the real state, not the settings.</summary>
    bool IsEnabled();

    /// <summary>Makes Windows launch the application, minimized, at sign in. Failures are logged, not thrown.</summary>
    Task EnableAsync();

    /// <summary>Stops Windows from launching the application at sign in. Failures are logged, not thrown.</summary>
    Task DisableAsync();
}
