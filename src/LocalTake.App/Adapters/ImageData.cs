using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LocalTake.Core.Ports;

namespace LocalTake.App.Adapters;

/// <summary>
/// A capture as LocalTake moves it around: its pixels, and the Avalonia bitmap made from them on demand. One pixel is
/// one physical screen pixel; the bitmap is at 96 DPI so that one Avalonia unit on the editor canvas is one pixel.
/// </summary>
internal sealed class ImageData(PixelBuffer pixels) : IImage
{
    private Bitmap? _bitmap;

    public PixelBuffer Pixels { get; } = pixels;
    public int Width => Pixels.Width;
    public int Height => Pixels.Height;

    public Bitmap Bitmap => _bitmap ??= ToBitmap(Pixels);

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    public static ImageData Of(IImage image) =>
        image as ImageData ?? throw new ArgumentException("Not an image of this application.", nameof(image));

    public static unsafe Bitmap ToBitmap(PixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        fixed (byte* data = pixels.Pixels)
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, (nint)data, new PixelSize(pixels.Width, pixels.Height),
                new Vector(96, 96), pixels.Stride);
    }

    /// <summary>The pixels of a rendered bitmap (for instance the editor's export).</summary>
    public static unsafe PixelBuffer FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var size = bitmap.PixelSize;
        var pixels = new byte[size.Width * size.Height * 4];
        fixed (byte* data = pixels)
            bitmap.CopyPixels(new PixelRect(size), (nint)data, pixels.Length, size.Width * 4);
        // Rendered over an opaque capture, every pixel is opaque: premultiplied and straight alpha are the same.
        return new PixelBuffer(size.Width, size.Height, pixels);
    }
}
