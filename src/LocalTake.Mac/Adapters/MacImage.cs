using CoreGraphics;
using LocalTake.Core.Ports;
using LocalTake.Platform.MacOS;

namespace LocalTake.Mac.Adapters;

/// <summary>An image as a CGImage, at its pixel size: one pixel is one physical screen pixel.</summary>
internal sealed class MacImage(CGImage image) : IImage
{
    /// <summary>Straight-alpha BGRA, as PixelBuffer holds it: kCGImageAlphaFirst | kCGBitmapByteOrder32Little.</summary>
    private const CGBitmapFlags StraightBgra = CGBitmapFlags.First | CGBitmapFlags.ByteOrder32Little;

    /// <summary>Premultiplied BGRA, the only BGRA a bitmap context draws into.</summary>
    private const CGBitmapFlags PremultipliedBgra = CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little;

    public CGImage Image { get; } = image;
    public int Width => (int)Image.Width;
    public int Height => (int)Image.Height;

    public void Dispose() => Image.Dispose();

    public static CGImage Unwrap(IImage image) =>
        (image as MacImage)?.Image ?? throw new ArgumentException("Not a macOS image.", nameof(image));

    public static MacImage FromPixels(PixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var provider = new CGDataProvider(pixels.Pixels);
        return new MacImage(new CGImage(pixels.Width, pixels.Height, 8, 32, pixels.Stride, colorSpace, StraightBgra,
            provider, null, false, CGColorRenderingIntent.Default));
    }

    /// <summary>Any CGImage as straight-alpha BGRA pixels, in sRGB.</summary>
    public static PixelBuffer ToPixels(CGImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int width = (int)image.Width, height = (int)image.Height;
        var pixels = new byte[width * height * 4];
        using (var colorSpace = CGColorSpace.CreateSrgb())
        using (var context = new CGBitmapContext(pixels, width, height, 8, width * 4, colorSpace, PremultipliedBgra))
            context.DrawImage(new CGRect(0, 0, width, height), image);
        MacPixels.Unpremultiply(pixels);
        return new PixelBuffer(width, height, pixels);
    }
}
