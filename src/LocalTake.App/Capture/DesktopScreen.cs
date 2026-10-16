using Avalonia.Controls;
using Avalonia.Platform;
using LocalTake.App.Adapters;
using LocalTake.Core.Capture;
using LocalTake.Core.Ports;
using CorePixelRect = LocalTake.Core.Geometry.PixelRect;

namespace LocalTake.App.Capture;

/// <summary>The monitors as Avalonia reports them, in physical pixels.</summary>
internal sealed class Monitors
{
    // A window that is never shown: Avalonia lists the screens through a top-level window.
    private readonly Lazy<Window> _probe = new(() => new Window());

    public IReadOnlyList<Screen> All => _probe.Value.Screens.All;

    public CorePixelRect VirtualScreen
    {
        get
        {
            var all = All;
            if (all.Count == 0)
                return new CorePixelRect(0, 0, 1, 1);
            var left = all.Min(s => s.Bounds.X);
            var top = all.Min(s => s.Bounds.Y);
            var right = all.Max(s => s.Bounds.Right);
            var bottom = all.Max(s => s.Bounds.Bottom);
            return new CorePixelRect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>The working area (without taskbar, menu bar or Dock) of the monitor showing most of an area.</summary>
    public CorePixelRect WorkAreaFor(CorePixelRect area)
    {
        var screen = All.OrderByDescending(s => Overlap(s.Bounds.ToCore(), area)).FirstOrDefault();
        return screen?.WorkingArea.ToCore() ?? VirtualScreen;
    }

    private static long Overlap(CorePixelRect a, CorePixelRect b)
    {
        var common = a.Intersect(b);
        return (long)common.Width * common.Height;
    }
}

/// <summary>Captures and area selection on the desktop.</summary>
internal sealed class DesktopScreen(IScreenGrabber grabber, Monitors monitors, INativeWindows nativeWindows) : IScreen, IAreaPicker
{
    public CorePixelRect VirtualScreen => monitors.VirtualScreen;

    public IImage Capture(CorePixelRect area) => new ImageData(grabber.Grab(area));

    /// <summary>Freezes every monitor, then lets the user drag an area or click a window on any of them.</summary>
    public async Task<AreaSelection?> PickAsync(bool windowMode)
    {
        var virtualScreen = VirtualScreen;
        var frozen = grabber.Grab(virtualScreen);
        var model = new SelectionModel(SelectionModel.ToLocal(grabber.VisibleWindows(), virtualScreen), windowMode);
        using var overlay = new SelectionOverlay(frozen, virtualScreen, monitors.All, model, nativeWindows);
        if (await overlay.RunAsync() is not { } local)
            return null;
        return new AreaSelection(local.Offset(virtualScreen.X, virtualScreen.Y), new ImageData(frozen.Crop(local)));
    }
}

internal static class AvaloniaGeometry
{
    public static CorePixelRect ToCore(this Avalonia.PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
