using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;

namespace ShowTime.Core.Tests.Ports;

public sealed class PixelBufferTests
{
    /// <summary>A 4×3 image whose blue byte is the pixel's index.</summary>
    private static PixelBuffer Numbered()
    {
        var pixels = new byte[4 * 3 * 4];
        for (var i = 0; i < 12; i++)
        {
            pixels[i * 4] = (byte)i;
            pixels[i * 4 + 3] = 255;
        }
        return new PixelBuffer(4, 3, pixels);
    }

    [Fact]
    public void Crop_copies_the_rows_of_the_area()
    {
        var crop = Numbered().Crop(new PixelRect(1, 1, 2, 2));
        crop.Width.ShouldBe(2);
        crop.Height.ShouldBe(2);
        Enumerable.Range(0, 4).Select(i => crop.Pixels[i * 4]).ShouldBe(new byte[] { 5, 6, 9, 10 });
    }

    [Fact]
    public void Crop_outside_the_image_is_refused() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Numbered().Crop(new PixelRect(3, 0, 2, 1)));

    [Fact]
    public void Pixelate_averages_each_block_of_the_area()
    {
        // 4×2 area, two columns and one row: blocks of 2×2 pixels.
        var pixels = new byte[4 * 2 * 4];
        byte[] blues = [0, 100, 50, 60, 200, 100, 70, 80];
        for (var i = 0; i < 8; i++)
        {
            pixels[i * 4] = blues[i];
            pixels[i * 4 + 3] = 255;
        }

        var blocks = new PixelBuffer(4, 2, pixels).Pixelate(new PixelRect(0, 0, 4, 2), columns: 2, rows: 1);

        blocks.Width.ShouldBe(4);
        blocks.Height.ShouldBe(2);
        Enumerable.Range(0, 8).Select(i => blocks.Pixels[i * 4]).ShouldBe(new byte[] { 100, 100, 65, 65, 100, 100, 65, 65 });
        blocks.Pixels[3].ShouldBe((byte)255);
    }

    [Fact]
    public void Pixelate_spreads_the_remainder_over_the_last_blocks()
    {
        var blocks = Numbered().Pixelate(new PixelRect(0, 0, 3, 3), columns: 2, rows: 1);
        blocks.Width.ShouldBe(3);
        blocks.Height.ShouldBe(3);
    }

    [Fact]
    public void The_pixel_count_must_match_the_size() =>
        Should.Throw<ArgumentException>(() => new PixelBuffer(2, 2, new byte[15]));
}
