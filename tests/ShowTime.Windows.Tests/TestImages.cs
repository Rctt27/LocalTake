using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShowTime.Windows.Tests;

internal static class TestImages
{
    /// <summary>An opaque 96-DPI image whose neighbouring pixels all differ.</summary>
    public static BitmapSource Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)x;
                pixels[i + 1] = (byte)y;
                pixels[i + 2] = (byte)(x * 7 + y * 13);
                pixels[i + 3] = 255;
            }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    /// <summary>The image's pixels as B, G, R, A bytes, rows top to bottom.</summary>
    public static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
