using ShowTime.Core.Geometry;

namespace ShowTime.Core.Ports;

/// <summary>The monitors, in physical pixels on the virtual desktop.</summary>
public interface IScreen
{
    /// <summary>The rectangle spanning every monitor.</summary>
    PixelRect VirtualScreen { get; }

    /// <summary>Captures an area of the virtual desktop.</summary>
    IImage Capture(PixelRect area);
}

/// <summary>An area chosen on the frozen screen, and its pixels as they were when the screen was frozen.</summary>
public sealed record AreaSelection(PixelRect Area, IImage Image);

/// <summary>Freezes the screen and lets the user pick an area or a window.</summary>
public interface IAreaPicker
{
    /// <returns>The chosen area, or null when the user cancelled.</returns>
    Task<AreaSelection?> PickAsync(bool windowMode);
}
