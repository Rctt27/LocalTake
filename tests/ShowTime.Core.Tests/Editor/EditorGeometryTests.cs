using ShowTime.Core.Editor;
using ShowTime.Core.Geometry;

namespace ShowTime.Core.Tests.Editor;

public sealed class EditorGeometryTests
{
    private static void ShouldBeNear(PointD actual, PointD expected)
    {
        actual.X.ShouldBe(expected.X, 1e-9);
        actual.Y.ShouldBe(expected.Y, 1e-9);
    }

    [Theory]
    [InlineData(100, 10, 100.498756, 0)]
    [InlineData(100, 90, 95.131488, 95.131488)]
    [InlineData(-10, 100, 0, 100.498756)]
    public void Snap45_keeps_the_length_on_the_nearest_eighth_of_a_turn(double x, double y, double expectedX, double expectedY)
    {
        var snapped = EditorGeometry.Snap45(new PointD(0, 0), new PointD(x, y));
        snapped.X.ShouldBe(expectedX, 1e-5);
        snapped.Y.ShouldBe(expectedY, 1e-5);
    }

    [Fact]
    public void RectBetween_works_in_every_drag_direction() =>
        EditorGeometry.RectBetween(new PointD(50, 50), new PointD(10, 20), square: false).ShouldBe(new RectD(10, 20, 40, 30));

    [Fact]
    public void RectBetween_square_keeps_the_larger_side_toward_the_pointer() =>
        EditorGeometry.RectBetween(new PointD(50, 50), new PointD(10, 40), square: true).ShouldBe(new RectD(10, 10, 40, 40));

    [Fact]
    public void Arrow_has_a_head_of_at_least_twelve_pixels()
    {
        var arrow = EditorGeometry.Arrow(new PointD(0, 0), new PointD(100, 0), thickness: 2)!;
        ShouldBeNear(arrow.ShaftStart, new PointD(0, 0));
        ShouldBeNear(arrow.ShaftEnd, new PointD(88 + 3.6, 0));
        ShouldBeNear(arrow.Tip, new PointD(100, 0));
        ShouldBeNear(arrow.HeadLeft, new PointD(88, 6.6));
        ShouldBeNear(arrow.HeadRight, new PointD(88, -6.6));
    }

    [Fact]
    public void Arrow_head_grows_with_the_thickness_but_not_beyond_the_length()
    {
        EditorGeometry.Arrow(new PointD(0, 0), new PointD(100, 0), thickness: 5)!.HeadLeft.X.ShouldBe(80, 1e-9);
        EditorGeometry.Arrow(new PointD(0, 0), new PointD(10, 0), thickness: 5)!.HeadLeft.X.ShouldBe(0, 1e-9);
    }

    [Fact]
    public void Arrow_shorter_than_a_pixel_is_nothing() =>
        EditorGeometry.Arrow(new PointD(5, 5), new PointD(5.5, 5), 2).ShouldBeNull();

    [Fact]
    public void ToPixelRect_rounds_outward_and_clips_to_the_image() =>
        EditorGeometry.ToPixelRect(new RectD(-3.5, 10.2, 50.6, 20.1), 40, 100).ShouldBe(new PixelRect(0, 10, 40, 21));

    [Fact]
    public void ToPixelRect_of_less_than_four_pixels_is_nothing() =>
        EditorGeometry.ToPixelRect(new RectD(10, 10, 3, 50), 100, 100).ShouldBeNull();

    [Theory]
    [InlineData(400, 300, 1.0, 24)]
    [InlineData(80, 80, 1.0, 10)]
    [InlineData(20, 20, 1.0, 6)]
    [InlineData(20, 20, 2.0, 12)]
    public void PixelationBlock_is_an_eighth_of_the_smaller_side_within_limits(int width, int height, double scale, int expected) =>
        EditorGeometry.PixelationBlock(new PixelRect(0, 0, width, height), scale).ShouldBe(expected);

    [Fact]
    public void PixelationGrid_has_at_least_one_cell() =>
        EditorGeometry.PixelationGrid(new PixelRect(0, 0, 5, 50), 6).ShouldBe((1, 8));

    [Fact]
    public void NextStepNumber_follows_the_highest_rather_than_the_count() =>
        EditorGeometry.NextStepNumber([1, 3]).ShouldBe(4);

    [Fact]
    public void NextStepNumber_starts_at_one() => EditorGeometry.NextStepNumber([]).ShouldBe(1);

    [Theory]
    [InlineData(0xFD, 0xD8, 0x35, true)]
    [InlineData(0xFF, 0xFF, 0xFF, true)]
    [InlineData(0xE5, 0x39, 0x35, false)]
    [InlineData(0, 0, 0, false)]
    public void Light_colours_get_dark_text(byte r, byte g, byte b, bool dark) =>
        EditorGeometry.NeedsDarkText(r, g, b).ShouldBe(dark);

    [Theory]
    [InlineData(0.01, 0.05)]
    [InlineData(20, 8)]
    [InlineData(1.5, 1.5)]
    public void Zoom_stays_between_five_percent_and_eight_times(double zoom, double expected) =>
        EditorGeometry.ClampZoom(zoom).ShouldBe(expected);

    [Fact]
    public void FitZoom_leaves_a_margin_and_never_enlarges_unless_asked()
    {
        EditorGeometry.FitZoom(1040, 540, 500, 250, dpiScale: 1, allowEnlarge: false).ShouldBe(1);
        EditorGeometry.FitZoom(1040, 540, 500, 250, dpiScale: 1, allowEnlarge: true).ShouldBe(2);
        EditorGeometry.FitZoom(1040, 540, 500, 250, dpiScale: 2, allowEnlarge: false).ShouldBe(0.5);
    }

    [Fact]
    public void Nearest_picks_the_closest_option() =>
        EditorGeometry.Nearest([2, 3, 4, 6, 8, 12], 7).ShouldBe(6);
}
