using AppKit;
using Foundation;
using LocalTake.Core.Geometry;
using LocalTake.Platform.MacOS;

namespace LocalTake.Mac.Platform;

/// <summary>A screen: its Quartz display, its Cocoa frame, and its rectangle in LocalTake's pixel space.</summary>
internal sealed record ScreenInfo(uint DisplayId, ScreenFrame Frame, PixelRect Pixels)
{
    /// <summary>The screens, main screen first. Reads AppKit: call it on the main thread.</summary>
    public static IReadOnlyList<ScreenInfo> Current()
    {
        var screens = NSScreen.Screens;
        var frames = screens
            .Select(s => new ScreenFrame(s.Frame.X, s.Frame.Y, s.Frame.Width, s.Frame.Height, s.BackingScaleFactor))
            .ToArray();
        var layout = MacScreenSpace.Layout(frames);
        return [.. screens.Select((screen, i) => new ScreenInfo(DisplayIdOf(screen), frames[i], layout[i]))];
    }

    public static uint DisplayIdOf(NSScreen screen) =>
        screen.DeviceDescription["NSScreenNumber"] is NSNumber number ? number.UInt32Value : 0;
}
