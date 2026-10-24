using LocalTake.Core.Geometry;

namespace LocalTake.Core.Recording;

/// <summary>
/// The mouse path drawn while the highlight trigger is held, like a highlighter stroke, and how it fades out once
/// released.
/// </summary>
public sealed class HighlightTrail
{
    /// <summary>The overlay's opacity while the trigger is held: the trail stays translucent over what it marks.</summary>
    public const double FullOpacity = 0.4;

    /// <summary>About half a minute of continuous movement; beyond that the oldest part goes.</summary>
    public const int MaxPoints = 2000;

    /// <summary>Pixels the mouse must move before a point is added.</summary>
    public const int MinStep = 2;

    public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(500);

    private readonly List<PixelPoint> _points = [];

    public IReadOnlyList<PixelPoint> Points => _points;

    public void Begin(PixelPoint at)
    {
        _points.Clear();
        _points.Add(at);
    }

    /// <returns>False when the point is too close to the last one to change the drawing.</returns>
    public bool Add(PixelPoint at)
    {
        if (_points.Count > 0)
        {
            var last = _points[^1];
            long dx = at.X - last.X, dy = at.Y - last.Y;
            if (dx * dx + dy * dy < MinStep * MinStep)
                return false;
        }
        _points.Add(at);
        if (_points.Count > MaxPoints)
            _points.RemoveAt(0);
        return true;
    }

    /// <summary>Fast at first, then gentle: an ease-out from <see cref="FullOpacity"/> to 0 over <see cref="FadeDuration"/>.</summary>
    public static double FadeOpacity(TimeSpan sinceRelease)
    {
        var remaining = 1 - Math.Clamp(sinceRelease / FadeDuration, 0, 1);
        return FullOpacity * remaining * remaining;
    }
}
