using System.Collections.Concurrent;
using VirtualDeskManager.Core.Models;
using VirtualDeskManager.Windows.Native;

namespace VirtualDeskManager.Windows.Overlay;

/// <summary>
/// Asks a window where its caption is actually free, instead of guessing how many buttons it has.
/// </summary>
/// <remarks>
/// Windows answers <c>WM_NCHITTEST</c> for windows of other processes, and applications that draw
/// their own caption answer it too. Visual Studio Code, for instance, reports its close, maximize
/// and minimize buttons, then <c>HTCLIENT</c> over its own layout buttons, and only past all of
/// them <c>HTCAPTION</c>. <c>HTCAPTION</c> is precisely "empty caption the user can drag the
/// window by", which is exactly where the floating button belongs.
///
/// The scan costs a handful of cross-process messages, so the result is cached per window and
/// only recalculated when the window or the size of its caption changes. Every message carries a
/// short timeout and the scan is abandoned at the first silence: a hung application can never
/// hold the message loop.
/// </remarks>
public static class CaptionProbe
{
    private const int StepPixels = 6;
    private const int MaximumScanPixels = 700;
    private const uint TimeoutMilliseconds = 40;
    private const int MaximumCacheEntries = 256;

    /// <summary>
    /// Whole budget for one scan. A window that answers slowly gets abandoned rather than
    /// holding up the message loop, and the estimate is used instead.
    /// </summary>
    private const long BudgetMilliseconds = 400;

    /// <summary>
    /// The width is bucketed so that resizing a window does not rescan on every single pixel.
    /// The layout of a caption only changes at fairly wide steps.
    /// </summary>
    private const int WidthBucket = 64;

    private static readonly ConcurrentDictionary<CacheKey, int> Cache = new();

    private readonly record struct CacheKey(nint Handle, int WidthBucket, int CaptionHeight);

    /// <summary>
    /// Pixels that have to stay free on the right of the caption so the button does not cover
    /// anything, or null when the window will not say.
    /// </summary>
    /// <param name="hwnd">The window whose caption is probed. Results are cached per window and caption size.</param>
    /// <param name="caption">The window's caption, in physical screen pixels.</param>
    /// <param name="requiredFreeWidth">Room the button needs, margins included.</param>
    public static int? MeasureReservedRight(nint hwnd, WindowBounds caption, int requiredFreeWidth)
    {
        CacheKey key = new(hwnd, caption.Width / WidthBucket, caption.Height);

        if (Cache.TryGetValue(key, out int cached))
        {
            return cached < 0 ? null : cached;
        }

        int? measured = Scan(hwnd, caption, requiredFreeWidth);

        if (Cache.Count > MaximumCacheEntries)
        {
            Cache.Clear();
        }

        Cache[key] = measured ?? -1;
        return measured;
    }

    /// <summary>Drops every cached measurement of a window, so the next one scans it again.</summary>
    public static void Forget(nint hwnd)
    {
        foreach (CacheKey key in Cache.Keys)
        {
            if (key.Handle == hwnd)
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Walks the caption from the right edge leftwards until it finds a free stretch wide enough.
    /// </summary>
    /// <remarks>
    /// It is a plain linear scan on purpose. A binary search looks tempting, but the free area is
    /// not a suffix: Visual Studio Code goes buttons, then its own layout buttons, then free
    /// caption, and then the search box again, so the answer is not monotonic and halving the
    /// interval lands in the wrong place. Measured on this machine, a cross-process hit test
    /// costs between 0.04 and 0.13 ms, so a whole scan is a couple of milliseconds and happens
    /// only when the window or the size of its caption changes.
    /// </remarks>
    private static int? Scan(nint hwnd, WindowBounds caption, int requiredFreeWidth)
    {
        int y = caption.Y + (caption.Height / 2);
        int limit = Math.Min(caption.Width - 40, MaximumScanPixels);
        long deadline = Environment.TickCount64 + BudgetMilliseconds;

        // Offset measured from the right edge of the caption, walking left.
        int freeRunStart = -1;

        for (int offset = 4; offset < limit; offset += StepPixels)
        {
            if (Environment.TickCount64 > deadline)
            {
                // A window this slow is not worth asking on every window change.
                return null;
            }

            if (!TryHitTest(hwnd, caption.Right - offset, y, out int hit))
            {
                // The window did not answer in time: better to fall back than to keep insisting.
                return null;
            }

            if (hit == NativeConstants.HTCAPTION)
            {
                if (freeRunStart < 0)
                {
                    freeRunStart = offset;
                }

                // A stretch long enough for the button and its margins.
                if (offset - freeRunStart >= requiredFreeWidth)
                {
                    return freeRunStart;
                }
            }
            else
            {
                // Anything else is taken: a system button, a button the application drew itself,
                // or its client area. The stretch starts over.
                freeRunStart = -1;
            }
        }

        return null;
    }

    private static bool TryHitTest(nint hwnd, int x, int y, out int hit)
    {
        hit = NativeConstants.HTNOWHERE;

        // lParam packs the point as two 16 bit halves, the low one being x.
        nint point = (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

        nint answered = User32.SendMessageTimeout(
            hwnd,
            NativeConstants.WM_NCHITTEST,
            0,
            point,
            NativeConstants.SMTO_ABORTIFHUNG,
            TimeoutMilliseconds,
            out nint result);

        if (answered == 0)
        {
            return false;
        }

        hit = (int)result;
        return true;
    }
}
