using System.Threading.Tasks;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using Forms = System.Windows.Forms;

namespace LocalTake.Adapters;

/// <summary>Captures of the virtual desktop, in physical pixels, and the area selection over a frozen copy of it.</summary>
internal sealed class WpfScreen(IScreenGrabber grabber) : IScreen, IAreaPicker
{
    public PixelRect VirtualScreen => Forms.SystemInformation.VirtualScreen.ToPixelRect();

    public IImage Capture(PixelRect area) => WpfImage.FromPixels(grabber.Grab(area));

    /// <summary>The overlay is modal: the selection is over when it returns.</summary>
    public Task<AreaSelection?> PickAsync(bool windowMode)
    {
        var virtualScreen = VirtualScreen;
        var frozen = grabber.Grab(virtualScreen);
        var area = SelectionOverlay.SelectArea(frozen, virtualScreen, grabber.VisibleWindows(), windowMode);
        AreaSelection? selection = area is { } selected
            ? new AreaSelection(selected, WpfImage.FromPixels(frozen.Crop(selected.Offset(-virtualScreen.X, -virtualScreen.Y))))
            : null;
        return Task.FromResult(selection);
    }
}
