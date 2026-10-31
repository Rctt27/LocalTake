using ShowTime.Core.Geometry;

namespace ShowTime.Core.Recording;

public static class RecordingBarPlacement
{
    /// <summary>Below the recorded area when there is room, otherwise above it, otherwise inside it.</summary>
    /// <param name="area">The recorded area, in physical screen pixels.</param>
    /// <param name="bar">The bar's size.</param>
    /// <param name="workArea">The work area of the monitor showing the area.</param>
    /// <param name="gap">Space between the area and the bar.</param>
    /// <returns>The bar's top-left corner.</returns>
    public static PixelPoint Place(PixelRect area, PixelSize bar, PixelRect workArea, int gap = 10)
    {
        var x = Math.Clamp(area.X + (area.Width - bar.Width) / 2, workArea.X, Math.Max(workArea.X, workArea.Right - bar.Width));
        int y;
        if (area.Bottom + gap + bar.Height <= workArea.Bottom)
            y = area.Bottom + gap;
        else if (area.Y - gap - bar.Height >= workArea.Y)
            y = area.Y - gap - bar.Height;
        else
            y = workArea.Bottom - bar.Height - gap;
        return new PixelPoint(x, y);
    }
}
