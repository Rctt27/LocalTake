using ShowTime.Core.Transcription;

namespace ShowTime.Core.Tests.Transcription;

public sealed class SubtitlePlacementTests
{
    /// <summary>A 16:9 video in a 1000x1000 host is shown 1000x562.5, with 218.75 of black above and below.</summary>
    [Fact]
    public void The_band_sits_on_the_picture_not_on_the_black_bars()
    {
        var placement = SubtitlePlacement.For(1000, 1000, 1920, 1080);

        placement.FontSize.ShouldBe(562.5 * 0.045, 1e-9);
        placement.MaxWidth.ShouldBe(900, 1e-9);
        placement.BottomMargin.ShouldBe(218.75 + 562.5 * 0.05, 1e-9);
    }

    [Fact]
    public void The_font_is_never_below_12() => SubtitlePlacement.For(400, 100, 0, 0).FontSize.ShouldBe(12);

    [Fact]
    public void A_video_of_unknown_size_fills_the_host()
    {
        var placement = SubtitlePlacement.For(800, 600, 0, 0);

        placement.MaxWidth.ShouldBe(720, 1e-9);
        placement.BottomMargin.ShouldBe(30, 1e-9);
    }
}
