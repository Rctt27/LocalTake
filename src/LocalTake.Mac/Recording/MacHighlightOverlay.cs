using AppKit;
using CoreGraphics;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Mac.Platform;

namespace LocalTake.Mac.Recording;

/// <summary>
/// The highlight trail over the recorded area: a transparent window that clicks go through, above every other one, and
/// on purpose not excluded from capture, unlike the red frame: ffmpeg records it with the screen. The window's opacity
/// makes the stroke translucent and fades it out without redrawing.
/// </summary>
internal sealed class MacHighlightOverlay : IHighlightOverlay
{
    private readonly NSWindow _window;
    private readonly TrailView _view;

    public MacHighlightOverlay(PixelRect area, INativeWindows windows)
    {
        var screen = ScreenInfo.Holding(area);
        var frame = screen.ToCocoa(area);
        _view = new TrailView(area, screen.Frame.Scale) { Frame = new CGRect(CGPoint.Empty, frame.Size) };
        _window = new NSWindow(frame, NSWindowStyle.Borderless, NSBackingStore.Buffered, false)
        {
            IsOpaque = false,
            BackgroundColor = NSColor.Clear,
            HasShadow = false,
            IgnoresMouseEvents = true,
            AlphaValue = (nfloat)HighlightTrail.FullOpacity,
            ContentView = _view
        };
        _window.ReleaseWhenClosed(false);
        windows.RaiseAboveEverything(_window.Handle);
    }

    public void Draw(IReadOnlyList<PixelPoint> points)
    {
        _view.Points = [.. points];
        _view.NeedsDisplay = true;
        if (!_window.IsVisible)
        {
            // Drawn before it shows: the video captures it, and its last stroke must not flash for a frame.
            _view.Display();
            _window.OrderFrontRegardless();
        }
    }

    public void SetOpacity(double opacity) => _window.AlphaValue = (nfloat)opacity;

    public void Hide()
    {
        _view.Points = [];
        _window.OrderOut(null);
    }

    public void Dispose()
    {
        _window.OrderOut(null);
        _window.Close();
        _window.Dispose();
        _view.Dispose();
    }

    /// <summary>The stroke, drawn in the window's points from LocalTake's pixels.</summary>
    private sealed class TrailView(PixelRect area, double scale) : NSView
    {
        private static readonly NSColor Ink = NSColor.FromSrgb(1f, 212f / 255, 0f, 1f);

        /// <summary>The same width as on Windows: 14.4 at 100 %, here in points, which the screen's scale multiplies.</summary>
        private const float Thickness = 14.4f;

        public PixelPoint[] Points { get; set; } = [];

        public override void DrawRect(CGRect dirtyRect)
        {
            var points = Points;
            if (points.Length == 0)
                return;
            // Cocoa's origin is at the bottom left of the view.
            CGPoint At(PixelPoint p) => new((p.X - area.X) / scale, (area.Bottom - p.Y) / scale);
            if (points.Length == 1)
            {
                var centre = At(points[0]);
                Ink.SetFill();
                NSBezierPath.FromOvalInRect(new CGRect(centre.X - Thickness / 2, centre.Y - Thickness / 2, Thickness, Thickness)).Fill();
                return;
            }
            var path = new NSBezierPath { LineWidth = Thickness, LineCapStyle = NSLineCapStyle.Round, LineJoinStyle = NSLineJoinStyle.Round };
            path.MoveTo(At(points[0]));
            foreach (var point in points.Skip(1))
                path.LineTo(At(point));
            Ink.SetStroke();
            path.Stroke();
        }
    }
}

internal sealed class MacHighlightOverlayFactory(INativeWindows windows) : IHighlightOverlayFactory
{
    public IHighlightOverlay Create(PixelRect area) => new MacHighlightOverlay(area, windows);
}
