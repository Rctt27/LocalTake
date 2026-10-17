using System.Diagnostics;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;

namespace LocalTake.Platform.MacOS;

/// <summary>
/// Screen capture through the system's own "screencapture" tool, decoded with ImageIO. macOS works in points while
/// LocalTake works in physical pixels: the conversion uses the main display's backing scale.
/// </summary>
/// <remarks>Needs the "Screen Recording" permission, which macOS asks for on first use.</remarks>
public sealed unsafe class MacScreenGrabber : IScreenGrabber
{
    public PixelBuffer Grab(PixelRect area)
    {
        var scale = MainDisplayScale();
        var file = Path.Combine(Path.GetTempPath(), "localtake-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var region = FormattableString.Invariant(
                $"{area.X / scale:0.##},{area.Y / scale:0.##},{area.Width / scale:0.##},{area.Height / scale:0.##}");
            // -x: no sound; -R: the region in points; -t png: lossless.
            using (var process = Process.Start(new ProcessStartInfo("/usr/sbin/screencapture", ["-x", "-R", region, "-t", "png", file])
            { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (process is null || !process.WaitForExit(10_000) || process.ExitCode != 0 || !File.Exists(file))
                    throw new InvalidOperationException(
                        "La capture d'écran a échoué. Autorisez LocalTake dans Réglages Système > Confidentialité et sécurité > Enregistrement de l'écran.");
            }
            return MacImages.Decode(File.ReadAllBytes(file), area.Width, area.Height)
                   ?? throw new InvalidOperationException("La capture d'écran est illisible.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public IReadOnlyList<PixelRect> VisibleWindows()
    {
        var scale = MainDisplayScale();
        var windows = new List<PixelRect>();
        var list = Quartz.CGWindowListCopyWindowInfo(Quartz.kCGWindowListOptionOnScreenOnly | Quartz.kCGWindowListExcludeDesktopElements, 0);
        if (list == 0)
            return windows;
        var boundsKey = Quartz.CFString("kCGWindowBounds");
        var layerKey = Quartz.CFString("kCGWindowLayer");
        try
        {
            var count = Quartz.CFArrayGetCount(list);
            for (nint i = 0; i < count; i++)
            {
                var info = Quartz.CFArrayGetValueAtIndex(list, i);
                // Layer 0 holds ordinary windows; the menu bar, the Dock and overlays live above it.
                int layer = 0;
                var layerValue = Quartz.CFDictionaryGetValue(info, layerKey);
                if (layerValue != 0)
                    Quartz.CFNumberGetValue(layerValue, Quartz.kCFNumberSInt32Type, &layer);
                if (layer != 0)
                    continue;
                var boundsValue = Quartz.CFDictionaryGetValue(info, boundsKey);
                if (boundsValue == 0 || !Quartz.CGRectMakeWithDictionaryRepresentation(boundsValue, out var r))
                    continue;
                var bounds = new PixelRect(
                    (int)Math.Round(r.Origin.X * scale), (int)Math.Round(r.Origin.Y * scale),
                    (int)Math.Round(r.Size.Width * scale), (int)Math.Round(r.Size.Height * scale));
                if (bounds.Width > 1 && bounds.Height > 1)
                    windows.Add(bounds);
            }
        }
        finally
        {
            Quartz.CFRelease(boundsKey);
            Quartz.CFRelease(layerKey);
            Quartz.CFRelease(list);
        }
        return windows;
    }

    /// <summary>Pixels per point of the main display: 2 on a Retina screen.</summary>
    internal static double MainDisplayScale()
    {
        var display = Quartz.CGMainDisplayID();
        var points = Quartz.CGDisplayBounds(display).Size.Width;
        return points > 0 ? Quartz.CGDisplayPixelsWide(display) / points : 1;
    }
}

/// <summary>Encoding and decoding images with ImageIO, as PixelBuffer's BGRA.</summary>
internal static unsafe class MacImages
{
    /// <summary>Decodes a PNG, TIFF or JPEG, scaled to the requested size when given.</summary>
    public static PixelBuffer? Decode(byte[] encoded, int width = 0, int height = 0)
    {
        nint data;
        fixed (byte* bytes = encoded)
            data = Quartz.CFDataCreate(0, bytes, encoded.Length);
        var source = Quartz.CGImageSourceCreateWithData(data, 0);
        Quartz.CFRelease(data);
        if (source == 0)
            return null;
        var image = Quartz.CGImageSourceCreateImageAtIndex(source, 0, 0);
        Quartz.CFRelease(source);
        if (image == 0)
            return null;
        try
        {
            if (width <= 0 || height <= 0)
                (width, height) = (CGImageWidth(image), CGImageHeight(image));
            var pixels = new byte[width * height * 4];
            var space = Quartz.CGColorSpaceCreateDeviceRGB();
            fixed (byte* target = pixels)
            {
                var context = Quartz.CGBitmapContextCreate(target, (nuint)width, (nuint)height, 8, (nuint)(width * 4), space, Quartz.BgraBitmapInfo);
                Quartz.CGContextDrawImage(context, new CGRect(0, 0, width, height), image);
                Quartz.CGContextRelease(context);
            }
            Quartz.CGColorSpaceRelease(space);
            return new PixelBuffer(width, height, pixels);
        }
        finally
        {
            Quartz.CGImageRelease(image);
        }
    }

    /// <summary>Encodes as PNG, for the pasteboard.</summary>
    public static byte[] EncodePng(PixelBuffer image)
    {
        var space = Quartz.CGColorSpaceCreateDeviceRGB();
        nint cgImage;
        fixed (byte* source = image.Pixels)
        {
            var context = Quartz.CGBitmapContextCreate(source, (nuint)image.Width, (nuint)image.Height, 8, (nuint)image.Stride, space, Quartz.BgraBitmapInfo);
            cgImage = Quartz.CGBitmapContextCreateImage(context);
            Quartz.CGContextRelease(context);
        }
        Quartz.CGColorSpaceRelease(space);

        var data = Quartz.CFDataCreateMutable(0, 0);
        var type = Quartz.CFString("public.png");
        var destination = Quartz.CGImageDestinationCreateWithData(data, type, 1, 0);
        Quartz.CGImageDestinationAddImage(destination, cgImage, 0);
        Quartz.CGImageDestinationFinalize(destination);
        var bytes = new ReadOnlySpan<byte>(Quartz.CFDataGetBytePtr(data), (int)Quartz.CFDataGetLength(data)).ToArray();
        Quartz.CFRelease(destination);
        Quartz.CFRelease(type);
        Quartz.CFRelease(data);
        Quartz.CGImageRelease(cgImage);
        return bytes;
    }

    private static int CGImageWidth(nint image) => (int)Quartz.CGImageGetWidth(image);

    private static int CGImageHeight(nint image) => (int)Quartz.CGImageGetHeight(image);
}
