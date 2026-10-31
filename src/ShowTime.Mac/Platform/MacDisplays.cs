using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;
using ShowTime.Platform.MacOS;

namespace ShowTime.Mac.Platform;

/// <summary>The screens in the order ffmpeg numbers them "Capture screen N": CGGetActiveDisplayList's.</summary>
internal sealed class MacDisplays : IDisplays
{
    public IReadOnlyList<PixelRect> Screens()
    {
        var screens = ScreenInfo.Current();
        var order = MacScreenSpace.InDisplayOrder([.. screens.Select(s => s.DisplayId)], ActiveDisplays.List());
        return [.. order.Select(i => screens[i].Pixels)];
    }
}
