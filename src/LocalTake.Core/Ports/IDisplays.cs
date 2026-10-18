using LocalTake.Core.Geometry;

namespace LocalTake.Core.Ports;

/// <summary>The screens, as the recording numbers them.</summary>
public interface IDisplays
{
    /// <summary>In the recording's order (usually the main screen first), each one in physical pixels of the virtual desktop.</summary>
    IReadOnlyList<PixelRect> Screens();
}
