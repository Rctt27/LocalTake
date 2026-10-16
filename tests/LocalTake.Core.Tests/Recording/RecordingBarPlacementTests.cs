using LocalTake.Core.Geometry;
using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class RecordingBarPlacementTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);
    private static readonly PixelSize Bar = new(400, 50);

    [Fact]
    public void Below_the_area_and_centred_when_there_is_room() =>
        RecordingBarPlacement.Place(new PixelRect(500, 100, 600, 400), Bar, WorkArea).ShouldBe(new PixelPoint(600, 510));

    [Fact]
    public void Above_the_area_when_it_reaches_the_bottom() =>
        RecordingBarPlacement.Place(new PixelRect(500, 600, 600, 420), Bar, WorkArea).ShouldBe(new PixelPoint(600, 540));

    [Fact]
    public void Inside_the_bottom_of_the_screen_when_the_area_fills_it() =>
        RecordingBarPlacement.Place(WorkArea, Bar, WorkArea).ShouldBe(new PixelPoint(760, 980));

    [Fact]
    public void Kept_on_the_screen_horizontally() =>
        RecordingBarPlacement.Place(new PixelRect(-50, 100, 100, 100), Bar, WorkArea).X.ShouldBe(0);

    [Fact]
    public void A_bar_wider_than_the_screen_sticks_to_its_left_edge() =>
        RecordingBarPlacement.Place(new PixelRect(0, 100, 100, 100), new PixelSize(3000, 50), WorkArea).X.ShouldBe(0);
}
