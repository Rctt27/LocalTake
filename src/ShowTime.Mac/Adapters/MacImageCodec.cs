using Foundation;
using ImageIO;
using ShowTime.Core.Ports;

namespace ShowTime.Mac.Adapters;

/// <summary>PNG or JPEG through ImageIO.</summary>
internal sealed class MacImageCodec : IImageCodec
{
    public void Save(IImage image, string path, int jpegQuality)
    {
        var isJpeg = Path.GetExtension(path).ToUpperInvariant() is ".JPG" or ".JPEG";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var url = NSUrl.FromFilename(path);
        using var destination = CGImageDestination.Create(url, isJpeg ? "public.jpeg" : "public.png", 1)
            ?? throw new IOException("Impossible d'écrire " + path);
        destination.AddImage(MacImage.Unwrap(image),
            isJpeg ? new CGImageDestinationOptions { LossyCompressionQuality = Math.Clamp(jpegQuality, 1, 100) / 100f } : null);
        if (!destination.Close())
            throw new IOException("Impossible d'écrire " + path);
    }
}
