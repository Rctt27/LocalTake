namespace ShowTime.Core.Geometry;

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>
/// A rectangle in physical screen pixels, as every screen coordinate in ShowTime (virtual desktop, windows, saved
/// region). Right and Bottom are exclusive. Its JSON form (X, Y, Width, Height) is the one settings.json uses.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The rectangle spanning two pixels, both included, whatever the drag direction.</summary>
    public static PixelRect Between(PixelPoint a, PixelPoint b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return new PixelRect(left, top, Math.Max(a.X, b.X) + 1 - left, Math.Max(a.Y, b.Y) + 1 - top);
    }

    public bool Contains(PixelPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    /// <summary>The common part, or an empty rectangle.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : default;
    }

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>H.264 in yuv420p needs even dimensions.</summary>
    public PixelRect WithEvenSize() => this with { Width = Math.Max(2, Width & ~1), Height = Math.Max(2, Height & ~1) };
}
