using System.Threading.Tasks;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using Forms = System.Windows.Forms;

namespace LocalTake.Adapters;

/// <summary>GDI capture of the virtual desktop, in physical pixels.</summary>
internal sealed class WpfScreen : IScreen, IAreaPicker
{
    public PixelRect VirtualScreen => Forms.SystemInformation.VirtualScreen.ToPixelRect();

    public IImage Capture(PixelRect area)
    {
        using var bitmap = ScreenCapture.Capture(area.ToRectangle());
        return new WpfImage(ScreenCapture.ToBitmapSource(bitmap));
    }

    /// <summary>The overlay is modal: the selection is over when it returns.</summary>
    public Task<AreaSelection?> PickAsync(bool windowMode)
    {
        using var frozen = SelectionOverlay.SelectArea(windowMode, out var area);
        AreaSelection? selection = frozen is null
            ? null
            : new AreaSelection(area.ToPixelRect(), new WpfImage(ScreenCapture.ToBitmapSource(frozen)));
        return Task.FromResult(selection);
    }
}
