using CoreGraphics;
using Foundation;
using ImageIO;
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

    /// <summary>An image file, as LocalTake's own images are: sRGB pixels. Null when macOS cannot read it.</summary>
    public static MacImage? Load(string path)
    {
        using var url = NSUrl.FromFilename(path);
        return Decode(CGImageSource.FromUrl(url));
    }

    /// <summary>Encoded image data (PNG, TIFF…), from the pasteboard for instance. Null when unreadable.</summary>
    public static MacImage? Decode(NSData data) => Decode(CGImageSource.FromData(data));

    private static MacImage? Decode(CGImageSource? source)
    {
        using (source)
        {
            using var image = source?.CreateImage(0, (CGImageOptions?)null);
            return image is null ? null : FromPixels(ToPixels(image));
        }
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
