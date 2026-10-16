using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LocalTake.App.Adapters;
using LocalTake.Core.Capture;
using CorePoint = LocalTake.Core.Geometry.PixelPoint;
using LocalTake.Core.Ports;
using CorePixelRect = LocalTake.Core.Geometry.PixelRect;

namespace LocalTake.App.Capture;

/// <summary>
/// The frozen screen, dimmed except for the area being chosen. One borderless window per monitor (a macOS window
/// cannot span several), all showing the same <see cref="SelectionModel"/> in physical pixels relative to the virtual
/// desktop's corner.
/// </summary>
internal sealed class SelectionOverlay : IDisposable
{
    private readonly SelectionModel _model;
    private readonly Bitmap _frozen;
    private readonly List<OverlayWindow> _windows = [];
    private readonly TaskCompletionSource<CorePixelRect?> _result = new();

    public SelectionOverlay(PixelBuffer frozen, CorePixelRect virtualScreen, IReadOnlyList<Screen> screens, SelectionModel model, INativeWindows nativeWindows)
    {
        _model = model;
        _frozen = ImageData.ToBitmap(frozen);
        foreach (var screen in screens)
        {
            var bounds = screen.Bounds.ToCore();
            var window = new OverlayWindow(this, bounds, bounds.Offset(-virtualScreen.X, -virtualScreen.Y), nativeWindows);
            _windows.Add(window);
        }
    }

    internal SelectionModel Model => _model;
    internal Bitmap Frozen => _frozen;

    /// <returns>The chosen area relative to the virtual desktop's corner, or null when cancelled.</returns>
    public Task<CorePixelRect?> RunAsync()
    {
        foreach (var window in _windows)
            window.Show();
        // Keyboard input goes to the active overlay; the one under the mouse takes over as it enters.
        _windows.FirstOrDefault()?.Activate();
        return _result.Task;
    }

    /// <summary>Passes the input to the model, then closes or repaints every monitor.</summary>
    internal void Apply(Action input)
    {
        input();
        switch (_model.Outcome)
        {
            case SelectionOutcome.Accepted:
                Finish(_model.Result);
                break;
            case SelectionOutcome.Cancelled:
                Finish(null);
                break;
            default:
                foreach (var window in _windows)
                    window.Surface.InvalidateVisual();
                break;
        }
    }

    private void Finish(CorePixelRect? result)
    {
        if (_result.Task.IsCompleted)
            return;
        foreach (var window in _windows)
            window.Close();
        _result.TrySetResult(result);
    }

    public void Dispose()
    {
        Finish(null);
        _frozen.Dispose();
    }

    private sealed class OverlayWindow : Window
    {
        private readonly SelectionOverlay _overlay;
        private readonly CorePixelRect _screenBounds;

        public OverlayWindow(SelectionOverlay overlay, CorePixelRect screenBounds, CorePixelRect local, INativeWindows nativeWindows)
        {
            _overlay = overlay;
            _screenBounds = screenBounds;
            Local = local;
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            Cursor = new Cursor(StandardCursorType.Cross);
            Position = new PixelPoint(screenBounds.X, screenBounds.Y);
            Surface = new OverlaySurface(overlay, this);
            Content = Surface;
            FitToScreen(1);

            Opened += (_, _) =>
            {
                // The scaling is the monitor's only once the window is on it.
                FitToScreen(RenderScaling);
                if (TryGetPlatformHandle()?.Handle is { } handle)
                    nativeWindows.RaiseAboveEverything(handle);
            };
            ScalingChanged += (_, _) => FitToScreen(RenderScaling);
            PointerEntered += (_, _) => Activate();
            KeyDown += OnKeyDown;
            PointerPressed += OnPointerPressed;
            PointerMoved += (_, e) => overlay.Apply(() => overlay.Model.PointerMoved(ToLocal(e)));
            PointerReleased += OnPointerReleased;
        }

        /// <summary>This monitor's rectangle relative to the virtual desktop's corner.</summary>
        public CorePixelRect Local { get; }

        public OverlaySurface Surface { get; }

        private void FitToScreen(double scaling)
        {
            Position = new PixelPoint(_screenBounds.X, _screenBounds.Y);
            Width = _screenBounds.Width / scaling;
            Height = _screenBounds.Height / scaling;
        }

        /// <summary>A pointer position as physical pixels relative to the virtual desktop's corner.</summary>
        private CorePoint ToLocal(PointerEventArgs e)
        {
            var p = e.GetPosition(Surface);
            return new CorePoint(Local.X + (int)Math.Floor(p.X * RenderScaling), Local.Y + (int)Math.Floor(p.Y * RenderScaling));
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            SelectionKey? key = e.Key switch
            {
                Key.Escape => SelectionKey.Escape,
                Key.Space => SelectionKey.Space,
                Key.Enter => SelectionKey.Enter,
                _ => null
            };
            if (key is { } pressed)
            {
                e.Handled = true;
                _overlay.Apply(() => _overlay.Model.KeyPressed(pressed));
            }
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(Surface);
            if (point.Properties.IsRightButtonPressed)
                _overlay.Apply(_overlay.Model.RightPressed);
            else if (point.Properties.IsLeftButtonPressed)
                _overlay.Apply(() => _overlay.Model.LeftPressed(ToLocal(e)));
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
                _overlay.Apply(() => _overlay.Model.LeftReleased(ToLocal(e)));
        }
    }

    /// <summary>Draws this monitor's part of the frozen screen and of the selection.</summary>
    private sealed class OverlaySurface(SelectionOverlay overlay, OverlayWindow window) : Control
    {
        private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(255, 87, 34));
        private static readonly IPen AccentPen = new Pen(Accent, 2);
        private static readonly IPen CrosshairPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1, DashStyle.Dash);
        private static readonly IBrush HintBackground = new SolidColorBrush(Color.FromArgb(210, 30, 30, 30));

        public override void Render(DrawingContext context)
        {
            var scaling = window.RenderScaling;
            var local = window.Local;
            var size = Bounds.Size;
            context.DrawImage(overlay.Frozen, new Rect(local.X, local.Y, local.Width, local.Height), new Rect(size));

            var model = overlay.Model;
            if (model.Highlight is { } highlight)
            {
                var rect = ToDip(highlight, local, scaling);
                // Dimmed around the highlight, which keeps the frozen pixels as they are.
                context.DrawRectangle(Dim, null, new Rect(0, 0, size.Width, Math.Max(0, rect.Top)));
                context.DrawRectangle(Dim, null, new Rect(0, rect.Bottom, size.Width, Math.Max(0, size.Height - rect.Bottom)));
                context.DrawRectangle(Dim, null, new Rect(0, rect.Top, Math.Max(0, rect.Left), rect.Height));
                context.DrawRectangle(Dim, null, new Rect(rect.Right, rect.Top, Math.Max(0, size.Width - rect.Right), rect.Height));
                context.DrawRectangle(null, AccentPen, rect.Deflate(1));
                DrawSizeLabel(context, rect, highlight);
            }
            else
            {
                context.DrawRectangle(Dim, null, new Rect(size));
            }

            if (model.ShowCrosshair && model.Cursor is { } cursor && local.Contains(cursor))
            {
                var x = (cursor.X - local.X) / scaling;
                var y = (cursor.Y - local.Y) / scaling;
                context.DrawLine(CrosshairPen, new Point(x, 0), new Point(x, size.Height));
                context.DrawLine(CrosshairPen, new Point(0, y), new Point(size.Width, y));
            }

            if (model.Cursor is not { } pointer || local.Contains(pointer))
                DrawHint(context, model.WindowMode, size);
        }

        private static Rect ToDip(CorePixelRect r, CorePixelRect local, double scaling) =>
            new((r.X - local.X) / scaling, (r.Y - local.Y) / scaling, r.Width / scaling, r.Height / scaling);

        private static void DrawSizeLabel(DrawingContext context, Rect rect, CorePixelRect pixels)
        {
            var text = Text($"{pixels.Width} × {pixels.Height}", 13, FontWeight.Bold);
            var y = rect.Top - text.Height - 8;
            if (y < 0)
                y = rect.Top + 6;
            var box = new Rect(rect.Left, y, text.Width + 12, text.Height + 4);
            context.DrawRectangle(Accent, null, box);
            context.DrawText(text, new Point(box.X + 6, box.Y + 2));
        }

        private static void DrawHint(DrawingContext context, bool windowMode, Size size)
        {
            var text = Text(windowMode
                ? "Mode fenêtre : cliquez sur une fenêtre   ·   Espace : mode zone   ·   Échap : annuler"
                : "Glissez pour sélectionner une zone   ·   Clic : fenêtre sous le curseur   ·   Espace : mode fenêtre   ·   Échap : annuler",
                14, FontWeight.Normal);
            var box = new Rect((size.Width - text.Width) / 2 - 12, 16, text.Width + 24, text.Height + 12);
            context.DrawRectangle(HintBackground, null, box, 4, 4);
            context.DrawText(text, new Point(box.X + 12, box.Y + 6));
        }

        private static FormattedText Text(string text, double size, FontWeight weight) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, weight: weight), size, Brushes.White);
    }
}
