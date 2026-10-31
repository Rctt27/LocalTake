// Pure geometry: it runs on every OS, although the assembly is marked for macOS.
#pragma warning disable CA1416

using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;
using ShowTime.Platform.MacOS;

namespace ShowTime.Platform.Tests.MacOS;

/// <summary>Cocoa's screens (points, origin at the bottom left of the main screen) laid out in ShowTime's pixels.</summary>
public sealed class MacScreenSpaceTests
{
    private static readonly ScreenFrame Main = new(0, 0, 1440, 900, 2);

    /// <summary>The main screen at 2×, a second one at 1× on its right.</summary>
    private static readonly ScreenFrame[] TwoScreens = [Main, new ScreenFrame(1440, 0, 1920, 1080, 1)];

    [Fact]
    public void A_point_on_the_main_screen_is_scaled_from_the_top_left() =>
        MacScreenSpace.PointToPixel(TwoScreens, MacScreenSpace.Layout(TwoScreens), 100, 800).ShouldBe(new PixelPoint(200, 200));

    [Fact]
    public void A_point_on_the_second_screen_uses_its_scale()
    {
        var layout = MacScreenSpace.Layout(TwoScreens);
        MacScreenSpace.PointToPixel(TwoScreens, layout, 1540, 980)
            .ShouldBe(MacScreenSpace.ToPixel(TwoScreens[1], layout[1], 1540, 980));
    }

    [Fact]
    public void A_point_off_every_screen_uses_the_nearest()
    {
        var layout = MacScreenSpace.Layout(TwoScreens);
        MacScreenSpace.PointToPixel(TwoScreens, layout, -50, 450).ShouldBe(MacScreenSpace.ToPixel(Main, layout[0], -50, 450));
        MacScreenSpace.PointToPixel(TwoScreens, layout, 3500, 1000)
            .ShouldBe(MacScreenSpace.ToPixel(TwoScreens[1], layout[1], 3500, 1000));
    }

    [Fact]
    public void Single_retina_screen() =>
        MacScreenSpace.Layout([new ScreenFrame(0, 0, 1512, 982, 2)]).ShouldBe([new PixelRect(0, 0, 3024, 1964)]);

    [Fact]
    public void Screen_above_the_main_one_has_a_negative_Y()
    {
        var layout = MacScreenSpace.Layout([Main, new ScreenFrame(0, 900, 1920, 1080, 1)]);

        layout[0].ShouldBe(new PixelRect(0, 0, 2880, 1800));
        layout[1].ShouldBe(new PixelRect(0, -2160, 1920, 1080));
    }

    [Fact]
    public void Mixed_scales_never_overlap()
    {
        var layout = MacScreenSpace.Layout([Main, new ScreenFrame(1440, 0, 1920, 1080, 1)]);

        layout[1].X.ShouldBe(2880);
        layout[1].Width.ShouldBe(1920);
        layout[1].Intersect(layout[0]).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Screen_on_the_left_has_a_negative_X()
    {
        var layout = MacScreenSpace.Layout([Main, new ScreenFrame(-1920, 0, 1920, 1080, 1)]);

        layout[1].ShouldBe(new PixelRect(-3840, -360, 1920, 1080));
        layout[1].Intersect(layout[0]).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Point_to_pixel_uses_the_screen_scale() =>
        MacScreenSpace.ToPixel(Main, new PixelRect(0, 0, 2880, 1800), 100, 850).ShouldBe(new PixelPoint(200, 100));

    [Fact]
    public void Point_on_a_secondary_screen_is_relative_to_its_layout()
    {
        var secondary = new ScreenFrame(1440, 0, 1920, 1080, 1);
        var layout = MacScreenSpace.Layout([Main, secondary]);

        MacScreenSpace.ToPixel(secondary, layout[1], 1440 + 10, 1080 - 20).ShouldBe(new PixelPoint(2880 + 10, -360 + 20));
    }

    [Fact]
    public void Screens_follow_the_display_list_ffmpeg_numbers()
    {
        // NSScreen lists 5 then 9; ffmpeg's "Capture screen 0" is display 9.
        MacScreenSpace.InDisplayOrder([5, 9], [9, 5]).ShouldBe([1, 0]);
    }

    [Fact]
    public void Screens_missing_from_the_display_list_come_last()
    {
        MacScreenSpace.InDisplayOrder([5, 7, 9], [9]).ShouldBe([2, 0, 1]);
    }

    [Fact]
    public void No_screen_gives_an_empty_layout() => MacScreenSpace.Layout([]).ShouldBeEmpty();

    [Fact]
    public void Window_frame_on_a_retina_screen_doubles()
    {
        ScreenFrame[] screens = [Main];

        MacScreenSpace.FromTopLeftPoints(screens, MacScreenSpace.Layout(screens), 10, 20, 100, 50)
            .ShouldBe(new PixelRect(20, 40, 200, 100));
    }

    [Fact]
    public void Window_frame_uses_the_screen_holding_its_centre()
    {
        ScreenFrame[] screens = [Main, new ScreenFrame(1440, 0, 1920, 1080, 1)];

        // Window coordinates have their origin at the top left of the main screen: the right screen starts at Y -180.
        MacScreenSpace.FromTopLeftPoints(screens, MacScreenSpace.Layout(screens), 1450, -160, 100, 50)
            .ShouldBe(new PixelRect(2890, -340, 100, 50));
    }

    [Fact]
    public void Pixels_to_a_Cocoa_frame_on_a_retina_screen()
    {
        var frame = MacScreenSpace.ToCocoa(Main, new PixelRect(0, 0, 2880, 1800), new PixelRect(200, 100, 400, 300));

        frame.ShouldBe(new ScreenFrame(100, 700, 200, 150, 2));
    }

    [Fact]
    public void Pixels_to_a_Cocoa_frame_on_a_secondary_screen()
    {
        var secondary = new ScreenFrame(1440, 0, 1920, 1080, 1);
        var layout = MacScreenSpace.Layout([Main, secondary]);

        MacScreenSpace.ToCocoa(secondary, layout[1], new PixelRect(2890, -340, 100, 50))
            .ShouldBe(new ScreenFrame(1450, 1010, 100, 50, 1));
    }

    [Fact]
    public void Cocoa_frame_back_to_pixels()
    {
        var laidOut = new PixelRect(0, 0, 2880, 1800);
        var area = new PixelRect(200, 100, 400, 300);

        MacScreenSpace.FromCocoa(Main, laidOut, MacScreenSpace.ToCocoa(Main, laidOut, area)).ShouldBe(area);
    }

    [Fact]
    public void Captures_of_two_screens_make_one_image()
    {
        var left = Solid(2, 2, 10);
        var right = Solid(2, 2, 20);

        var image = MacScreenSpace.Compose(new PixelRect(1, 0, 2, 1),
            [(new PixelRect(0, 0, 2, 2), left), (new PixelRect(2, 0, 2, 2), right)]);

        image.Width.ShouldBe(2);
        image.Pixels.ShouldBe(new byte[] { 10, 10, 10, 255, 20, 20, 20, 255 });
    }

    [Fact]
    public void Area_outside_every_screen_is_opaque_black()
    {
        var image = MacScreenSpace.Compose(new PixelRect(1, 0, 2, 1), [(new PixelRect(0, 0, 2, 2), Solid(2, 2, 10))]);

        image.Pixels[4..].ShouldBe(new byte[] { 0, 0, 0, 255 });
    }

    private static PixelBuffer Solid(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (value, value, value, 255);
        return new PixelBuffer(width, height, pixels);
    }
}
