using ShowTime.Core.Geometry;

namespace ShowTime.Core.Tests.Geometry;

public sealed class PixelRectTests
{
    [Fact]
    public void Between_two_points_includes_both_pixels() =>
        PixelRect.Between(new PixelPoint(10, 20), new PixelPoint(5, 30)).ShouldBe(new PixelRect(5, 20, 6, 11));

    [Fact]
    public void Contains_includes_the_left_and_top_edges_only()
    {
        var rect = new PixelRect(0, 0, 10, 10);
        rect.Contains(new PixelPoint(0, 0)).ShouldBeTrue();
        rect.Contains(new PixelPoint(9, 9)).ShouldBeTrue();
        rect.Contains(new PixelPoint(10, 5)).ShouldBeFalse();
    }

    [Fact]
    public void Intersect_of_disjoint_rectangles_is_empty() =>
        new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(20, 20, 5, 5)).IsEmpty.ShouldBeTrue();

    [Fact]
    public void Intersect_keeps_the_common_part() =>
        new PixelRect(-100, 50, 800, 600).Intersect(new PixelRect(0, 0, 1920, 1080)).ShouldBe(new PixelRect(0, 50, 700, 600));

    [Fact]
    public void Offset_moves_the_rectangle() =>
        new PixelRect(1, 2, 3, 4).Offset(10, -2).ShouldBe(new PixelRect(11, 0, 3, 4));

    [Theory]
    [InlineData(641, 481, 640, 480)]
    [InlineData(640, 480, 640, 480)]
    [InlineData(1, 1, 2, 2)]
    public void WithEvenSize_rounds_down_to_even_dimensions_of_at_least_two(int width, int height, int expectedWidth, int expectedHeight) =>
        new PixelRect(3, 4, width, height).WithEvenSize().ShouldBe(new PixelRect(3, 4, expectedWidth, expectedHeight));

    [Fact]
    public void Right_and_Bottom_are_exclusive()
    {
        var rect = new PixelRect(10, 20, 30, 40);
        rect.Right.ShouldBe(40);
        rect.Bottom.ShouldBe(60);
    }
}
