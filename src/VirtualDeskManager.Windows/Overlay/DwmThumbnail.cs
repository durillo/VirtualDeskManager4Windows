using VirtualDeskManager.Core.Desktops;
using VirtualDeskManager.Core.Diagnostics;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Overlay;

/// <summary>
/// A live picture of another window, drawn inside one of ours by the desktop compositor.
/// </summary>
/// <remarks>
/// This is what makes the desktop overview possible at all. Nothing here copies pixels: DWM
/// already holds a composed surface for every window it draws, and registering a thumbnail just
/// asks it to paint that same surface somewhere else as well. Opening the overview therefore
/// costs a handful of calls rather than a screenshot per window, and what appears is live rather
/// than a still.
///
/// It keeps working for windows the shell has cloaked because they live on another virtual
/// desktop, which is the whole reason the overview can show them at all.
///
/// Two limits come with it, and both shape the interface built on top:
/// the thumbnail is painted over everything the destination window draws in that rectangle, so
/// captions and borders have to sit outside it; and the destination must not be layered, so the
/// overlay cannot be translucent.
/// </remarks>
public sealed class DwmThumbnail : IDisposable
{
    private readonly IAppLogger _logger;
    private nint _handle;
    private LayoutRect _placedAt;
    private byte _opacity;
    private bool _visible;

    private DwmThumbnail(nint handle, nint source, SIZE sourceSize, IAppLogger logger)
    {
        _handle = handle;
        _logger = logger;
        Source = source;
        SourceSize = sourceSize;
    }

    /// <summary>The window being shown.</summary>
    public nint Source { get; }

    /// <summary>Size of the source window as the compositor sees it.</summary>
    public SIZE SourceSize { get; }

    /// <summary>
    /// Width over height of the source, or 0 when it could not be measured.
    /// </summary>
    /// <remarks>
    /// The caller needs this before deciding where the thumbnail goes: DWM stretches to whatever
    /// destination it is handed, so the shape has to be worked out beforehand.
    /// </remarks>
    public double AspectRatio => SourceSize.cy > 0 ? SourceSize.cx / (double)SourceSize.cy : 0;

    /// <summary>
    /// Registers a thumbnail of <paramref name="source"/> inside <paramref name="destination"/>,
    /// or null when the compositor refuses.
    /// </summary>
    /// <remarks>
    /// A refusal is ordinary: the window may have closed between being listed and being shown,
    /// and an elevated window is off limits to a process that is not. Neither is worth an error
    /// dialog, so both come back as null and the overview simply leaves that slot empty.
    /// </remarks>
    public static DwmThumbnail? Register(nint destination, nint source, IAppLogger? logger = null)
    {
        logger ??= NullAppLogger.Instance;

        if (destination == 0 || source == 0 || destination == source)
        {
            return null;
        }

        try
        {
            int result = Dwmapi.DwmRegisterThumbnail(destination, source, out nint handle);

            if (result != 0 || handle == 0)
            {
                logger.Debug(
                    LogEvents.NativeApiError,
                    ("Where", nameof(Dwmapi.DwmRegisterThumbnail)),
                    ("Source", source),
                    ("Result", result));

                return null;
            }

            SIZE size = default;

            if (Dwmapi.DwmQueryThumbnailSourceSize(handle, out SIZE queried) == 0)
            {
                size = queried;
            }

            return new DwmThumbnail(handle, source, size, logger);
        }
        catch (Exception ex)
        {
            logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Register)), ("Source", source));
            return null;
        }
    }

    /// <summary>
    /// Moves the thumbnail to <paramref name="rect"/>, in physical pixels relative to the client
    /// area of the destination window, and shows it.
    /// </summary>
    /// <remarks>
    /// Cheap enough to call on every mouse move: this is what lets a dragged window follow the
    /// cursor without anything being redrawn by this process.
    /// </remarks>
    /// <param name="rect">Where to draw it, in physical pixels relative to the destination's client area. Empty hides it.</param>
    /// <param name="opacity">
    /// 255 is solid. Anything less is the only way to grey a thumbnail down, since a veil painted
    /// over it would be hidden underneath it.
    /// </param>
    public void Place(LayoutRect rect, byte opacity = 255)
    {
        if (_handle == 0)
        {
            return;
        }

        if (rect.IsEmpty)
        {
            Hide();
            return;
        }

        // Nothing to say to the compositor when neither the place nor the look changed.
        if (_visible && rect == _placedAt && opacity == _opacity)
        {
            return;
        }

        DWM_THUMBNAIL_PROPERTIES properties = new()
        {
            dwFlags = Dwmapi.DWM_TNP_RECTDESTINATION
                | Dwmapi.DWM_TNP_VISIBLE
                | Dwmapi.DWM_TNP_OPACITY
                | Dwmapi.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT
            {
                Left = rect.Left,
                Top = rect.Top,
                Right = rect.Right,
                Bottom = rect.Bottom
            },
            opacity = opacity,
            fVisible = true,

            // False keeps the caption of the source window in the picture, which is what makes
            // one Explorer window tellable from the next at thumbnail size.
            fSourceClientAreaOnly = false
        };

        if (Update(ref properties))
        {
            _placedAt = rect;
            _opacity = opacity;
            _visible = true;
        }
    }

    /// <summary>Takes the thumbnail off screen without giving it up.</summary>
    public void Hide()
    {
        if (_handle == 0 || !_visible)
        {
            return;
        }

        DWM_THUMBNAIL_PROPERTIES properties = new()
        {
            dwFlags = Dwmapi.DWM_TNP_VISIBLE,
            fVisible = false
        };

        if (Update(ref properties))
        {
            _visible = false;
        }
    }

    private bool Update(ref DWM_THUMBNAIL_PROPERTIES properties)
    {
        try
        {
            return Dwmapi.DwmUpdateThumbnailProperties(_handle, ref properties) == 0;
        }
        catch (Exception ex)
        {
            _logger.Error(LogEvents.NativeApiError, ex, ("Where", nameof(Update)), ("Source", Source));
            return false;
        }
    }

    /// <summary>Unregisters the thumbnail, which stops the compositor painting it. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        try
        {
            Dwmapi.DwmUnregisterThumbnail(_handle);
        }
        catch (Exception ex)
        {
            // The destination window may already be gone, which unregisters them all anyway.
            _logger.Debug(LogEvents.NativeApiError, ("Where", nameof(Dispose)), ("Error", ex.Message));
        }

        _handle = 0;
        _visible = false;
    }
}
