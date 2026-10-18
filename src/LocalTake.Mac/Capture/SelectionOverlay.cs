using AppKit;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using LocalTake.Core.Capture;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Mac.Adapters;
using LocalTake.Mac.Platform;

namespace LocalTake.Mac.Capture;

/// <summary>
/// The frozen screens, dimmed, above everything, the menu bar and the Dock included: one borderless window per
/// screen. A selection stays on the screen where it started, as with macOS's own screenshot tool. The rules are
/// <see cref="SelectionModel"/>'s, one per screen, in that screen's pixels.
/// </summary>
internal sealed class SelectionOverlay
{
    private readonly List<OverlayView> _views = [];
    private readonly TaskCompletionSource<PixelRect?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OverlayView? _active;

    private SelectionOverlay()
    {
    }

    /// <param name="frozen">The screens as captured, <paramref name="virtualScreen"/> in size.</param>
    /// <returns>The chosen area in LocalTake's pixel space, or null when the user cancelled.</returns>
    public static Task<PixelRect?> SelectAreaAsync(PixelBuffer frozen, PixelRect virtualScreen, IReadOnlyList<ScreenInfo> screens,
        IReadOnlyList<PixelRect> windows, bool windowMode, INativeWindows native)
    {
        var overlay = new SelectionOverlay();
        foreach (var screen in screens)
        {
            using var image = MacImage.FromPixels(frozen.Crop(screen.Pixels.Offset(-virtualScreen.X, -virtualScreen.Y)));
            var model = new SelectionModel(SelectionModel.ToLocal(windows, screen.Pixels), windowMode);
            var frame = new CGRect(screen.Frame.X, screen.Frame.Y, screen.Frame.Width, screen.Frame.Height);
            var window = new OverlayWindow(frame);
            var view = new OverlayView(overlay, window, screen, model, new NSImage(image.Image, frame.Size))
            {
                Frame = new CGRect(CGPoint.Empty, frame.Size)
            };
            window.ContentView = view;
            native.ExcludeFromCapture(window.Handle);
            native.RaiseAboveEverything(window.Handle);
            overlay._views.Add(view);
        }

        NSApplication.SharedApplication.Activate();
        foreach (var view in overlay._views)
            view.Host.OrderFrontRegardless();
        // The keyboard goes to the screen under the mouse.
        var start = overlay._views.FirstOrDefault(v => v.HasMouse()) ?? overlay._views.FirstOrDefault();
        if (start is not null)
            overlay.Activate(start);
        return overlay._completion.Task;
    }

    private void Activate(OverlayView view)
    {
        _active = view;
        view.Host.MakeKeyAndOrderFront(null);
        view.Host.MakeFirstResponder(view);
    }

    private void MouseEntered(OverlayView view)
    {
        if (_active != view)
            Activate(view);
    }

    /// <summary>Escape and Space act on every screen, so that the mode stays the same everywhere.</summary>
    private void KeyPressed(OverlayView view, SelectionKey key)
    {
        if (key == SelectionKey.Enter)
            view.Model.KeyPressed(key);
        else
            foreach (var each in _views)
                each.Model.KeyPressed(key);
        Check(view);
    }

    /// <summary>Ends the selection once a screen's model has an outcome.</summary>
    private void Check(OverlayView view)
    {
        foreach (var each in _views)
            each.NeedsDisplay = true;
        var outcome = view.Model.Outcome;
        if (outcome == SelectionOutcome.Pending)
            return;
        var area = outcome == SelectionOutcome.Accepted && view.Model.Result is { } local
            ? local.Offset(view.Screen.Pixels.X, view.Screen.Pixels.Y)
            : (PixelRect?)null;
        var views = _views.ToList();
        _views.Clear();
        foreach (var each in views)
        {
            each.Host.OrderOut(null);
            each.Host.Close();
        }
        // Released once the event that ended the selection has returned: AppKit is still inside one of these windows.
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            foreach (var each in views)
            {
                each.Host.Dispose();
                each.Dispose();
            }
        });
        _completion.TrySetResult(area);
    }

    private sealed class OverlayWindow : NSWindow
    {
        public OverlayWindow(CGRect frame) : base(frame, NSWindowStyle.Borderless, NSBackingStore.Buffered, false)
        {
            // Closed and disposed from .NET: AppKit must not release it a second time.
            ReleaseWhenClosed(false);
            Opaque = true;
            HasShadow = false;
            AcceptsMouseMovedEvents = true;
        }

        public override bool CanBecomeKeyWindow => true;

        public override bool CanBecomeMainWindow => true;

        /// <summary>Over the menu bar too: AppKit would otherwise push the window below it.</summary>
        public override CGRect ConstrainFrameRect(CGRect frameRect, NSScreen? screen) => frameRect;
    }

    /// <summary>One screen: draws its frozen image and its model, and reports the input in that screen's pixels.</summary>
    private sealed class OverlayView : NSView
    {
        private static readonly NSColor Accent = NSColor.FromSrgb(1f, 87f / 255, 34f / 255, 1f);
        private static readonly NSColor Dim = NSColor.FromSrgb(0f, 0f, 0f, 0.47f);
        private static readonly NSColor Crosshair = NSColor.FromSrgb(1f, 1f, 1f, 0.55f);
        private static readonly NSColor HintBackground = NSColor.FromSrgb(30f / 255, 30f / 255, 30f / 255, 0.82f);

        private readonly SelectionOverlay _overlay;
        private readonly NSImage _image;

        public OverlayView(SelectionOverlay overlay, OverlayWindow host, ScreenInfo screen, SelectionModel model, NSImage image)
        {
            _overlay = overlay;
            Host = host;
            Screen = screen;
            Model = model;
            _image = image;
        }

        public OverlayWindow Host { get; }
        public ScreenInfo Screen { get; }
        public SelectionModel Model { get; }

        /// <summary>Top-left origin, as LocalTake's pixels.</summary>
        public override bool IsFlipped => true;

        public override bool AcceptsFirstResponder() => true;

        public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

        public bool HasMouse()
        {
            var mouse = NSEvent.CurrentMouseLocation;
            var f = Screen.Frame;
            return mouse.X >= f.X && mouse.X < f.X + f.Width && mouse.Y >= f.Y && mouse.Y < f.Y + f.Height;
        }

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();
            foreach (var area in TrackingAreas())
                RemoveTrackingArea(area);
            AddTrackingArea(new NSTrackingArea(Bounds,
                NSTrackingAreaOptions.MouseMoved | NSTrackingAreaOptions.MouseEnteredAndExited |
                NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.InVisibleRect, this, null));
        }

        public override void ResetCursorRects() => AddCursorRect(Bounds, NSCursor.CrosshairCursor);

        public override void MouseEntered(NSEvent theEvent) => _overlay.MouseEntered(this);

        public override void MouseMoved(NSEvent theEvent) => Pointer(theEvent, Model.PointerMoved);

        public override void MouseDragged(NSEvent theEvent) => Pointer(theEvent, Model.PointerMoved);

        public override void MouseDown(NSEvent theEvent) => Pointer(theEvent, Model.LeftPressed);

        public override void MouseUp(NSEvent theEvent) => Pointer(theEvent, Model.LeftReleased);

        public override void RightMouseDown(NSEvent theEvent)
        {
            Model.RightPressed();
            _overlay.Check(this);
        }

        public override void KeyDown(NSEvent theEvent)
        {
            // Virtual key codes: Escape, Space, Return, keypad Enter.
            SelectionKey? key = theEvent.KeyCode switch
            {
                53 => SelectionKey.Escape,
                49 => SelectionKey.Space,
                36 or 76 => SelectionKey.Enter,
                _ => null
            };
            if (key is { } pressed)
                _overlay.KeyPressed(this, pressed);
        }

        /// <summary>The event's position in this screen's pixels, kept on the screen while dragging past its edge.</summary>
        private void Pointer(NSEvent theEvent, Action<PixelPoint> handler)
        {
            var point = ConvertPointFromView(theEvent.LocationInWindow, null);
            var scale = Screen.Frame.Scale;
            var x = (int)Math.Clamp(Math.Round(point.X * scale), 0, Screen.Pixels.Width - 1);
            var y = (int)Math.Clamp(Math.Round(point.Y * scale), 0, Screen.Pixels.Height - 1);
            handler(new PixelPoint(x, y));
            _overlay.Check(this);
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var bounds = Bounds;
            _image.Draw(bounds, CGRect.Empty, NSCompositingOperation.Copy, 1, true, null);

            if (Model.Highlight is { } highlight)
            {
                var rect = ToPoints(highlight);
                var dimmed = NSBezierPath.FromRect(bounds);
                dimmed.AppendPath(NSBezierPath.FromRect(rect));
                dimmed.WindingRule = NSWindingRule.EvenOdd;
                Dim.SetFill();
                dimmed.Fill();
                Accent.SetStroke();
                var border = NSBezierPath.FromRect(rect.Inset(1, 1));
                border.LineWidth = 2;
                border.Stroke();
                DrawSizeLabel($"{highlight.Width} × {highlight.Height}", rect);
            }
            else
            {
                Dim.SetFill();
                NSBezierPath.FillRect(bounds);
            }

            if (Model.ShowCrosshair && Model.Cursor is { } cursor)
            {
                var point = ToPoints(new PixelRect(cursor.X, cursor.Y, 0, 0));
                Crosshair.SetStroke();
                NSBezierPath.StrokeLine(new CGPoint(point.X, 0), new CGPoint(point.X, bounds.Height));
                NSBezierPath.StrokeLine(new CGPoint(0, point.Y), new CGPoint(bounds.Width, point.Y));
            }

            DrawHint(bounds);
        }

        private CGRect ToPoints(PixelRect r)
        {
            var scale = Screen.Frame.Scale;
            return new CGRect(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale);
        }

        private static void DrawSizeLabel(string label, CGRect rect)
        {
            using var text = new NSAttributedString(label, new NSStringAttributes
            {
                Font = NSFont.BoldSystemFontOfSize(12),
                ForegroundColor = NSColor.White
            });
            var size = text.Size;
            double above = rect.Y - size.Height - 6;
            var box = new CGRect(rect.X, above < 0 ? rect.Y + 6 : above, size.Width + 10, size.Height + 2);
            Accent.SetFill();
            NSBezierPath.FillRect(box);
            text.DrawAtPoint(new CGPoint(box.X + 5, box.Y + 1));
        }

        private void DrawHint(CGRect bounds)
        {
            var hint = Model.WindowMode
                ? "Mode fenêtre : cliquez sur une fenêtre   ·   Espace : mode zone   ·   Échap : annuler"
                : "Glissez pour sélectionner une zone   ·   Clic : fenêtre sous le curseur   ·   Espace : mode fenêtre   ·   Échap : annuler";
            using var text = new NSAttributedString(hint, new NSStringAttributes
            {
                Font = NSFont.SystemFontOfSize(13),
                ForegroundColor = NSColor.White
            });
            var size = text.Size;
            // Below the menu bar and the camera housing.
            var box = new CGRect((bounds.Width - size.Width) / 2 - 12, 44, size.Width + 24, size.Height + 12);
            HintBackground.SetFill();
            NSBezierPath.FromRoundedRect(box, 6, 6).Fill();
            text.DrawAtPoint(new CGPoint(box.X + 12, box.Y + 6));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _image.Dispose();
            base.Dispose(disposing);
        }
    }
}
