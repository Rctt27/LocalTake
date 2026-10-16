using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;

namespace LocalTake.Adapters;

/// <summary>A frozen 96-DPI BitmapSource: one WPF unit is one image pixel.</summary>
internal sealed class WpfImage(BitmapSource source) : IImage
{
    public BitmapSource Source { get; } = source;
    public int Width => Source.PixelWidth;
    public int Height => Source.PixelHeight;

    /// <summary>A frozen BitmapSource holds no native resource.</summary>
    public void Dispose()
    {
    }

    public static BitmapSource Unwrap(IImage image) =>
        (image as WpfImage)?.Source ?? throw new ArgumentException("Not a WPF image.", nameof(image));
}

internal static class GeometryConversions
{
    public static PixelRect ToPixelRect(this Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    public static Rectangle ToRectangle(this PixelRect r) => new(r.X, r.Y, r.Width, r.Height);
}
