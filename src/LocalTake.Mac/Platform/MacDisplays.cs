using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;

namespace LocalTake.Mac.Platform;

/// <summary>The screens in NSScreen's order, which is the order ffmpeg numbers them "Capture screen N" in.</summary>
internal sealed class MacDisplays : IDisplays
{
    public IReadOnlyList<PixelRect> Screens() => [.. ScreenInfo.Current().Select(s => s.Pixels)];
}
