using AppKit;
using CoreGraphics;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Mac.Platform;

namespace LocalTake.Mac.Recording;

/// <summary>
/// The red frame around the recorded area: four thin windows just outside it, which clicks go through. Kept out of
/// captures, and outside the recorded area anyway, so that the video never shows them.
/// </summary>
internal sealed class RecordingFrame : IDisposable
{
    /// <summary>In points.</summary>
    private const int Thickness = 3;

    private static readonly NSColor Red = NSColor.FromSrgb(229f / 255, 57f / 255, 53f / 255, 1f);

    private readonly List<NSWindow> _strips = [];

    public RecordingFrame(PixelRect area, INativeWindows windows)
    {
        var screen = ScreenInfo.Holding(area);
        var t = (int)Math.Round(Thickness * screen.Frame.Scale);
        PixelRect[] strips =
        [
            new(area.X - t, area.Y - t, area.Width + 2 * t, t),
            new(area.X - t, area.Bottom, area.Width + 2 * t, t),
            new(area.X - t, area.Y, t, area.Height),
            new(area.Right, area.Y, t, area.Height)
        ];
        foreach (var strip in strips)
        {
            var window = new NSWindow(screen.ToCocoa(strip), NSWindowStyle.Borderless, NSBackingStore.Buffered, false)
            {
                BackgroundColor = Red,
                IsOpaque = true,
                HasShadow = false,
                IgnoresMouseEvents = true
            };
            window.ReleaseWhenClosed(false);
            windows.ExcludeFromCapture(window.Handle);
            windows.RaiseAboveEverything(window.Handle);
            window.OrderFrontRegardless();
            _strips.Add(window);
        }
    }

    public void Dispose()
    {
        foreach (var window in _strips)
        {
            window.OrderOut(null);
            window.Close();
            window.Dispose();
        }
        _strips.Clear();
    }
}
