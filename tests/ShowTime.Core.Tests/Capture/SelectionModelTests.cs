using ShowTime.Core.Capture;
using ShowTime.Core.Geometry;

namespace ShowTime.Core.Tests.Capture;

public sealed class SelectionModelTests
{
    private static readonly PixelRect Window = new(100, 100, 300, 200);

    private static SelectionModel Model(bool windowMode = false) => new([Window], windowMode);

    [Fact]
    public void Dragging_selects_the_area_between_the_two_points_inclusive()
    {
        var model = Model();
        model.LeftPressed(new PixelPoint(10, 10));
        model.PointerMoved(new PixelPoint(50, 40));
        model.LeftReleased(new PixelPoint(50, 40));

        model.Outcome.ShouldBe(SelectionOutcome.Accepted);
        model.Result.ShouldBe(new PixelRect(10, 10, 41, 31));
    }

    [Fact]
    public void Dragging_upwards_selects_the_same_area()
    {
        var model = Model();
        model.LeftPressed(new PixelPoint(50, 40));
        model.PointerMoved(new PixelPoint(10, 10));
        model.LeftReleased(new PixelPoint(10, 10));
        model.Result.ShouldBe(new PixelRect(10, 10, 41, 31));
    }

    [Fact]
    public void A_simple_click_takes_the_window_under_the_cursor()
    {
        var model = Model();
        model.LeftPressed(new PixelPoint(150, 150));
        model.LeftReleased(new PixelPoint(150, 150));
        model.Result.ShouldBe(Window);
    }

    [Fact]
    public void A_drag_smaller_than_three_pixels_counts_as_a_click()
    {
        var model = Model();
        model.LeftPressed(new PixelPoint(150, 150));
        model.PointerMoved(new PixelPoint(151, 151));
        model.LeftReleased(new PixelPoint(151, 151));
        model.Result.ShouldBe(Window);
    }

    [Fact]
    public void A_click_outside_any_window_keeps_the_overlay_open()
    {
        var model = Model();
        model.LeftPressed(new PixelPoint(5, 5));
        model.LeftReleased(new PixelPoint(5, 5));
        model.Outcome.ShouldBe(SelectionOutcome.Pending);
        model.IsDragging.ShouldBeFalse();
    }

    [Fact]
    public void In_window_mode_a_drag_still_takes_the_window_where_the_button_is_released()
    {
        var model = Model(windowMode: true);
        model.LeftPressed(new PixelPoint(10, 10));
        model.PointerMoved(new PixelPoint(200, 200));
        model.LeftReleased(new PixelPoint(200, 200));
        model.Result.ShouldBe(Window);
    }

    [Fact]
    public void Space_switches_between_area_and_window_mode_but_not_during_a_drag()
    {
        var model = Model();
        model.KeyPressed(SelectionKey.Space);
        model.WindowMode.ShouldBeTrue();
        model.LeftPressed(new PixelPoint(1, 1));
        model.KeyPressed(SelectionKey.Space);
        model.WindowMode.ShouldBeTrue();
    }

    [Fact]
    public void Escape_cancels()
    {
        var model = Model();
        model.KeyPressed(SelectionKey.Escape);
        model.Outcome.ShouldBe(SelectionOutcome.Cancelled);
    }

    [Fact]
    public void Enter_takes_the_hovered_window()
    {
        var model = Model();
        model.PointerMoved(new PixelPoint(120, 120));
        model.KeyPressed(SelectionKey.Enter);
        model.Result.ShouldBe(Window);
    }

    [Fact]
    public void Right_click_cancels_the_drag_first_then_the_overlay()
    {
        var model = Model();
        model.LeftPressed(new PixelPoint(1, 1));
        model.RightPressed();
        model.Outcome.ShouldBe(SelectionOutcome.Pending);
        model.IsDragging.ShouldBeFalse();
        model.RightPressed();
        model.Outcome.ShouldBe(SelectionOutcome.Cancelled);
    }

    [Fact]
    public void The_highlight_follows_the_drag_then_the_hovered_window()
    {
        var model = Model();
        model.PointerMoved(new PixelPoint(150, 150));
        model.Highlight.ShouldBe(Window);
        model.ShowCrosshair.ShouldBeTrue();

        model.LeftPressed(new PixelPoint(150, 150));
        model.PointerMoved(new PixelPoint(160, 170));
        model.Highlight.ShouldBe(new PixelRect(150, 150, 11, 21));
        model.ShowCrosshair.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_happens_after_the_outcome_is_decided()
    {
        var model = Model();
        model.KeyPressed(SelectionKey.Escape);
        model.LeftPressed(new PixelPoint(150, 150));
        model.LeftReleased(new PixelPoint(150, 150));
        model.Outcome.ShouldBe(SelectionOutcome.Cancelled);
    }

    [Fact]
    public void ToLocal_converts_windows_to_overlay_coordinates_and_drops_the_off_screen_ones()
    {
        var virtualScreen = new PixelRect(-1920, 0, 3840, 1080);
        PixelRect[] windows = [new(-2000, 10, 200, 100), new(5000, 0, 100, 100), new(0, 0, 1, 50)];
        SelectionModel.ToLocal(windows, virtualScreen).ShouldBe([new PixelRect(0, 10, 120, 100)]);
    }
}
