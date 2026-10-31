using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShowTime.Adapters;
using ShowTime.Core.Ports;

namespace ShowTime.Windows.Tests.Adapters;

public sealed class WpfImageTests
{
    private static byte[] Bgra32(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    [Fact]
    public void Pixels_round_trip_without_changing_colours()
    {
        byte[] pixels =
        [
            10, 20, 200, 255, 0, 0, 0, 255, 255, 255, 255, 255,
            1, 2, 3, 255, 128, 64, 32, 255, 90, 180, 45, 255
        ];

        var back = WpfImage.ToPixels(WpfImage.FromPixels(new PixelBuffer(3, 2, pixels)).Source);

        back.Width.ShouldBe(3);
        back.Height.ShouldBe(2);
        back.Pixels.ShouldBe(pixels);
    }

    [Fact]
    public void Red_stays_red()
    {
        var image = WpfImage.FromPixels(new PixelBuffer(1, 1, [0, 0, 255, 255]));

        Bgra32(image.Source).ShouldBe(new byte[] { 0, 0, 255, 255 });
    }

    [Fact]
    public void Translucent_pixels_are_not_premultiplied()
    {
        var back = WpfImage.ToPixels(WpfImage.FromPixels(new PixelBuffer(1, 1, [40, 100, 200, 128])).Source);

        back.Pixels.ShouldBe(new byte[] { 40, 100, 200, 128 });
    }

    [Fact]
    public void Images_are_frozen_and_96_dpi()
    {
        var image = WpfImage.FromPixels(new PixelBuffer(2, 2, new byte[16]));

        image.Source.IsFrozen.ShouldBeTrue();
        image.Source.DpiX.ShouldBe(96);
        image.Width.ShouldBe(2);
    }
}
