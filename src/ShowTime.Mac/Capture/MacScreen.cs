using ShowTime.Core.Capture;
using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;
using ShowTime.Mac.Adapters;
using ShowTime.Mac.Platform;

namespace ShowTime.Mac.Capture;

/// <summary>Captures of the screens, in ShowTime's pixel space, and the area selection over a frozen copy of them.</summary>
internal sealed class MacScreen(IScreenGrabber grabber, INativeWindows windows) : IScreen, IAreaPicker
{
    public PixelRect VirtualScreen => Bounds(ScreenInfo.Current());

    public IImage Capture(PixelRect area) => MacImage.FromPixels(grabber.Grab(area));

    public async Task<AreaSelection?> PickAsync(bool windowMode)
    {
        var screens = ScreenInfo.Current();
        var virtualScreen = Bounds(screens);
        var frozen = grabber.Grab(virtualScreen);
        var area = await SelectionOverlay.SelectAreaAsync(frozen, virtualScreen, screens, grabber.VisibleWindows(), windowMode, windows)
            .ConfigureAwait(true);
        return area is { } selected
            ? new AreaSelection(selected, MacImage.FromPixels(frozen.Crop(selected.Offset(-virtualScreen.X, -virtualScreen.Y))))
            : null;
    }

    /// <summary>The rectangle spanning every screen.</summary>
    private static PixelRect Bounds(IReadOnlyList<ScreenInfo> screens)
    {
        int left = screens.Min(s => s.Pixels.X), top = screens.Min(s => s.Pixels.Y);
        int right = screens.Max(s => s.Pixels.Right), bottom = screens.Max(s => s.Pixels.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}
