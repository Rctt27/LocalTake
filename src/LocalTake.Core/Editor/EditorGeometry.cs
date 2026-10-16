using LocalTake.Core.Geometry;

namespace LocalTake.Core.Editor;

/// <summary>A point on the editor canvas, in image pixels.</summary>
public readonly record struct PointD(double X, double Y);

/// <summary>A rectangle on the editor canvas, in image pixels.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height);

/// <summary>An arrow: an open shaft and a filled triangular head.</summary>
public sealed record ArrowShape(PointD ShaftStart, PointD ShaftEnd, PointD Tip, PointD HeadLeft, PointD HeadRight);

/// <summary>Shapes and sizes of the annotation editor. The canvas is in image pixels.</summary>
public static class EditorGeometry
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 8;

    /// <summary>Space kept around the image when it is fitted to the window.</summary>
    private const double FitMargin = 40;

    /// <summary>Shift: the segment keeps its length and snaps to the nearest multiple of 45°.</summary>
    public static PointD Snap45(PointD from, PointD to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var step = Math.PI / 4;
        var angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return new PointD(from.X + Math.Cos(angle) * length, from.Y + Math.Sin(angle) * length);
    }

    /// <summary>The rectangle dragged from <paramref name="a"/> to <paramref name="b"/>; square keeps the larger side.</summary>
    public static RectD RectBetween(PointD a, PointD b, bool square)
    {
        var w = Math.Abs(b.X - a.X);
        var h = Math.Abs(b.Y - a.Y);
        if (square)
            w = h = Math.Max(w, h);
        return new RectD(b.X < a.X ? a.X - w : a.X, b.Y < a.Y ? a.Y - h : a.Y, w, h);
    }

    /// <summary>The head grows with the stroke, from 12 pixels, but never beyond the arrow's length.</summary>
    /// <returns>Null when the arrow is shorter than a pixel.</returns>
    public static ArrowShape? Arrow(PointD from, PointD to, double thickness)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1)
            return null;
        var (ux, uy) = (dx / length, dy / length);
        var (nx, ny) = (-uy, ux);
        var head = Math.Min(length, Math.Max(12, thickness * 4));
        var baseX = to.X - ux * head;
        var baseY = to.Y - uy * head;
        var half = head * 0.55;
        return new ArrowShape(
            from,
            new PointD(baseX + ux * head * 0.3, baseY + uy * head * 0.3),
            to,
            new PointD(baseX + nx * half, baseY + ny * half),
            new PointD(baseX - nx * half, baseY - ny * half));
    }

    /// <summary>The frame of a blur or crop gesture as whole image pixels.</summary>
    /// <returns>Null when less than 4 pixels wide or high once clipped to the image.</returns>
    public static PixelRect? ToPixelRect(RectD frame, int imageWidth, int imageHeight)
    {
        var left = Math.Max(0, (int)Math.Floor(frame.X));
        var top = Math.Max(0, (int)Math.Floor(frame.Y));
        var right = Math.Min(imageWidth, (int)Math.Ceiling(frame.X + frame.Width));
        var bottom = Math.Min(imageHeight, (int)Math.Ceiling(frame.Y + frame.Height));
        return right - left >= 4 && bottom - top >= 4 ? new PixelRect(left, top, right - left, bottom - top) : null;
    }

    /// <summary>Blur is pixelation: blocks of an eighth of the smaller side, 6 to 24 screen pixels.</summary>
    public static int PixelationBlock(PixelRect area, double dpiScale) =>
        (int)Math.Clamp(Math.Min(area.Width, area.Height) / 8.0, 6 * dpiScale, 24 * dpiScale);

    public static (int Columns, int Rows) PixelationGrid(PixelRect area, int block) =>
        (Math.Max(1, area.Width / block), Math.Max(1, area.Height / block));

    /// <summary>Highest + 1 rather than a count: deleting badge 2 of 1-2-3 must not produce a second 3.</summary>
    public static int NextStepNumber(IEnumerable<int> existing) => existing.DefaultIfEmpty(0).Max() + 1;

    /// <summary>Whether a badge of this colour needs black text rather than white.</summary>
    public static bool NeedsDarkText(byte r, byte g, byte b) => (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.6;

    public static double ClampZoom(double zoom) => Math.Clamp(zoom, MinZoom, MaxZoom);

    /// <summary>
    /// The zoom that shows the whole image. Without <paramref name="allowEnlarge"/>, a small capture stays at its real
    /// size on screen (1 / DPI scale, since the canvas is in physical pixels).
    /// </summary>
    public static double FitZoom(double viewportWidth, double viewportHeight, int imageWidth, int imageHeight, double dpiScale, bool allowEnlarge)
    {
        var fit = Math.Min((viewportWidth - FitMargin) / imageWidth, (viewportHeight - FitMargin) / imageHeight);
        return ClampZoom(allowEnlarge ? fit : Math.Min(fit, 1 / dpiScale));
    }

    /// <summary>The option closest to a saved value; the first one on a tie.</summary>
    public static double Nearest(IReadOnlyList<double> options, double value) =>
        options.OrderBy(o => Math.Abs(o - value)).First();
}
