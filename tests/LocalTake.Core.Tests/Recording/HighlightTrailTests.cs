using LocalTake.Core.Geometry;
using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class HighlightTrailTests
{
    [Fact]
    public void Begin_starts_a_new_trail_at_the_point()
    {
        var trail = new HighlightTrail();
        trail.Begin(new PixelPoint(0, 0));
        trail.Add(new PixelPoint(10, 0));
        trail.Begin(new PixelPoint(50, 50));
        trail.Points.ShouldBe([new PixelPoint(50, 50)]);
    }

    [Fact]
    public void Points_closer_than_the_minimum_step_are_skipped()
    {
        var trail = new HighlightTrail();
        trail.Begin(new PixelPoint(0, 0));
        trail.Add(new PixelPoint(1, 1)).ShouldBeFalse();
        trail.Add(new PixelPoint(2, 0)).ShouldBeTrue();
        trail.Points.Count.ShouldBe(2);
    }

    /// <summary>
    /// The oldest quarter goes at once rather than one point per frame: each drop repaints the whole overlay, so it
    /// happens every 500 points instead of on every frame once the limit is reached.
    /// </summary>
    [Fact]
    public void Oldest_points_go_by_the_quarter_beyond_the_limit()
    {
        var trail = new HighlightTrail();
        trail.Begin(new PixelPoint(0, 0));
        for (var i = 1; i < HighlightTrail.MaxPoints; i++)
            trail.Add(new PixelPoint(i * 2, 0));
        trail.Points.Count.ShouldBe(HighlightTrail.MaxPoints);

        trail.Add(new PixelPoint(HighlightTrail.MaxPoints * 2, 0));
        trail.Points.Count.ShouldBe(HighlightTrail.MaxPoints - HighlightTrail.MaxPoints / 4 + 1);
        trail.Points[0].ShouldBe(new PixelPoint(HighlightTrail.MaxPoints / 4 * 2, 0));
        trail.Points[^1].ShouldBe(new PixelPoint(HighlightTrail.MaxPoints * 2, 0));
    }

    [Theory]
    [InlineData(0, 0.4)]
    [InlineData(250, 0.1)]
    [InlineData(500, 0)]
    [InlineData(2000, 0)]
    public void The_fade_is_fast_then_gentle(int milliseconds, double opacity) =>
        HighlightTrail.FadeOpacity(TimeSpan.FromMilliseconds(milliseconds)).ShouldBe(opacity, 1e-9);

    [Fact]
    public void The_fade_only_decreases()
    {
        var values = Enumerable.Range(0, 33).Select(i => HighlightTrail.FadeOpacity(TimeSpan.FromMilliseconds(i * 16))).ToList();
        values.ShouldBe([.. values.OrderByDescending(v => v)]);
    }
}
