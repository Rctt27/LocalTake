using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;

namespace LocalTake.Platform.MacOS;

/// <summary>A screen as Cocoa gives it: a frame in points, origin at the bottom left of the main screen, Y going up.</summary>
/// <param name="Scale">Pixels per point: 2 on a Retina screen.</param>
public sealed record ScreenFrame(double X, double Y, double Width, double Height, double Scale);

/// <summary>
/// Places the screens in LocalTake's space: physical pixels, origin at the top left of the main screen, Y going down.
/// </summary>
/// <remarks>
/// macOS has no single pixel space when screens have different scales. Origins are scaled by the largest scale and
/// sizes by each screen's own, so every screen keeps its full resolution and no two of them overlap; between screens
/// of lower scale the space has gaps, which no capture covers.
/// </remarks>
public static class MacScreenSpace
{
    /// <param name="screens">The main screen first, as <c>NSScreen.Screens</c> lists them.</param>
    public static IReadOnlyList<PixelRect> Layout(IReadOnlyList<ScreenFrame> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Count == 0)
            return [];
        var top = screens[0].Y + screens[0].Height;
        var scale = screens.Max(screen => screen.Scale);
        return [.. screens.Select(screen => new PixelRect(
            Round(screen.X * scale),
            Round((top - screen.Y - screen.Height) * scale),
            Round(screen.Width * screen.Scale),
            Round(screen.Height * screen.Scale)))];
    }

    /// <summary>A point in Cocoa's global coordinates, on <paramref name="screen"/>, as a pixel of the layout.</summary>
    /// <param name="laidOut">The screen's rectangle from <see cref="Layout"/>.</param>
    public static PixelPoint ToPixel(ScreenFrame screen, PixelRect laidOut, double pointX, double pointY)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return new PixelPoint(
            laidOut.X + Round((pointX - screen.X) * screen.Scale),
            laidOut.Y + Round((screen.Y + screen.Height - pointY) * screen.Scale));
    }

    /// <summary>A rectangle of the layout, on <paramref name="screen"/>, as a Cocoa frame in points.</summary>
    /// <param name="laidOut">The screen's rectangle from <see cref="Layout"/>.</param>
    /// <returns>The frame, with the screen's scale.</returns>
    public static ScreenFrame ToCocoa(ScreenFrame screen, PixelRect laidOut, PixelRect rect)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var scale = screen.Scale;
        double width = rect.Width / scale, height = rect.Height / scale;
        var top = (rect.Y - laidOut.Y) / scale;
        return new ScreenFrame(screen.X + (rect.X - laidOut.X) / scale, screen.Y + screen.Height - top - height, width, height, scale);
    }

    /// <summary>A Cocoa frame on <paramref name="screen"/> (its visible frame, for instance) as pixels of the layout.</summary>
    public static PixelRect FromCocoa(ScreenFrame screen, PixelRect laidOut, ScreenFrame frame)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(frame);
        var origin = ToPixel(screen, laidOut, frame.X, frame.Y + frame.Height);
        return new PixelRect(origin.X, origin.Y, Round(frame.Width * screen.Scale), Round(frame.Height * screen.Scale));
    }

    /// <summary>
    /// A rectangle in Quartz window coordinates (points, origin at the top left of the main screen, Y going down) as
    /// pixels of the layout, at the scale of the screen holding its centre.
    /// </summary>
    public static PixelRect FromTopLeftPoints(IReadOnlyList<ScreenFrame> screens, IReadOnlyList<PixelRect> layout,
        double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(layout);
        var top = screens[0].Y + screens[0].Height;
        // In Cocoa's coordinates, Y going up.
        double centreX = x + width / 2, centreY = top - (y + height / 2);
        var index = 0;
        for (var i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            if (centreX >= s.X && centreX < s.X + s.Width && centreY >= s.Y && centreY < s.Y + s.Height)
            {
                index = i;
                break;
            }
        }
        var screen = screens[index];
        var origin = ToPixel(screen, layout[index], x, top - y);
        return new PixelRect(origin.X, origin.Y, Round(width * screen.Scale), Round(height * screen.Scale));
    }

    /// <summary>
    /// Assembles the captures of the screens an area spans. What lies on no screen stays opaque black, like the gaps
    /// of the layout.
    /// </summary>
    /// <param name="captures">Each screen's rectangle in the layout, and its capture at that size.</param>
    public static PixelBuffer Compose(PixelRect area, IReadOnlyList<(PixelRect Screen, PixelBuffer Pixels)> captures)
    {
        ArgumentNullException.ThrowIfNull(captures);
        var pixels = new byte[area.Width * area.Height * 4];
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        foreach (var (screen, capture) in captures)
        {
            var part = screen.Intersect(area);
            if (part.IsEmpty)
                continue;
            var rowBytes = part.Width * 4;
            for (var y = 0; y < part.Height; y++)
                Array.Copy(capture.Pixels, (part.Y - screen.Y + y) * capture.Stride + (part.X - screen.X) * 4,
                    pixels, (part.Y - area.Y + y) * area.Width * 4 + (part.X - area.X) * 4, rowBytes);
        }
        return new PixelBuffer(area.Width, area.Height, pixels);
    }

    /// <summary>
    /// The order ffmpeg numbers the screens "Capture screen N" in: CGGetActiveDisplayList's, which need not be
    /// NSScreen's. Screens it does not list keep their order, after the others.
    /// </summary>
    /// <param name="screens">Each screen's display identifier, in NSScreen's order.</param>
    /// <param name="displayList">The active displays, in CGGetActiveDisplayList's order.</param>
    /// <returns>Indexes into <paramref name="screens"/>.</returns>
    public static IReadOnlyList<int> InDisplayOrder(IReadOnlyList<uint> screens, IReadOnlyList<uint> displayList)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(displayList);
        return [.. Enumerable.Range(0, screens.Count)
            .OrderBy(i => displayList.ToList().IndexOf(screens[i]) is var at and >= 0 ? at : int.MaxValue)
            .ThenBy(i => i)];
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
