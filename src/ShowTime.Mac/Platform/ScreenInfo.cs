using AppKit;
using CoreGraphics;
using Foundation;
using ShowTime.Core.Geometry;
using ShowTime.Platform.MacOS;

namespace ShowTime.Mac.Platform;

/// <summary>
/// A screen: its Quartz display, its Cocoa frame, its rectangle in ShowTime's pixel space, and the part of it the menu
/// bar and the Dock leave free.
/// </summary>
internal sealed record ScreenInfo(uint DisplayId, ScreenFrame Frame, PixelRect Pixels, PixelRect WorkArea)
{
    /// <summary>The screens, main screen first. Reads AppKit: call it on the main thread.</summary>
    public static IReadOnlyList<ScreenInfo> Current()
    {
        var screens = NSScreen.Screens;
        var frames = screens
            .Select(s => new ScreenFrame(s.Frame.X, s.Frame.Y, s.Frame.Width, s.Frame.Height, s.BackingScaleFactor))
            .ToArray();
        var layout = MacScreenSpace.Layout(frames);
        return [.. screens.Select((screen, i) =>
        {
            var visible = screen.VisibleFrame;
            var workArea = MacScreenSpace.FromCocoa(frames[i], layout[i],
                new ScreenFrame(visible.X, visible.Y, visible.Width, visible.Height, frames[i].Scale));
            return new ScreenInfo(DisplayIdOf(screen), frames[i], layout[i], workArea);
        })];
    }

    /// <summary>The screen holding most of the area: the one a recording of it uses.</summary>
    public static ScreenInfo Holding(PixelRect area)
    {
        var screens = Current();
        return screens.MaxBy(s =>
        {
            var common = s.Pixels.Intersect(area);
            return (long)common.Width * common.Height;
        }) ?? screens[0];
    }

    /// <summary>A rectangle of ShowTime's pixel space, on this screen, as a Cocoa frame.</summary>
    public CGRect ToCocoa(PixelRect rect)
    {
        var f = MacScreenSpace.ToCocoa(Frame, Pixels, rect);
        return new CGRect(f.X, f.Y, f.Width, f.Height);
    }

    public static uint DisplayIdOf(NSScreen screen) =>
        screen.DeviceDescription["NSScreenNumber"] is NSNumber number ? number.UInt32Value : 0;
}
