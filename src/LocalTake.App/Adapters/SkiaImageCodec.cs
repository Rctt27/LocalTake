using LocalTake.Core.Ports;
using SkiaSharp;

namespace LocalTake.App.Adapters;

/// <summary>PNG and JPEG through Skia, which Avalonia already ships on every platform.</summary>
internal sealed class SkiaImageCodec : IImageCodec
{
    public void Save(IImage image, string path, int jpegQuality)
    {
        var pixels = ImageData.Of(image).Pixels;
        var jpeg = Path.GetExtension(path).ToUpperInvariant() is ".JPG" or ".JPEG";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        Encode(pixels, stream, jpeg, jpegQuality);
    }

    public static unsafe void Encode(PixelBuffer pixels, Stream stream, bool jpeg, int jpegQuality)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        // JPEG has no alpha channel: the image is declared opaque.
        var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, jpeg ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
        fixed (byte* data = pixels.Pixels)
        {
            using var image = SKImage.FromPixels(new SKPixmap(info, (nint)data, pixels.Stride));
            using var encoded = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png,
                jpeg ? Math.Clamp(jpegQuality, 1, 100) : 100);
            encoded.SaveTo(stream);
        }
    }

    /// <summary>Reads a PNG, JPEG, BMP, GIF or WebP file.</summary>
    /// <returns>Null when the file is not an image Skia can read.</returns>
    public static PixelBuffer? Load(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
            return null;
        // Straight alpha, as PixelBuffer holds it: Skia would otherwise premultiply a transparent PNG.
        var info = codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Unpremul);
        using var bitmap = SKBitmap.Decode(codec, info);
        return bitmap is null ? null : new PixelBuffer(bitmap.Width, bitmap.Height, bitmap.Bytes);
    }
}
