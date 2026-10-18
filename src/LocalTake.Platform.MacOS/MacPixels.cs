namespace LocalTake.Platform.MacOS;

/// <summary>Pixel formats between Quartz and <see cref="Core.Ports.PixelBuffer"/>.</summary>
public static class MacPixels
{
    /// <summary>
    /// Quartz draws into premultiplied BGRA only; PixelBuffer holds straight alpha. Converts in place.
    /// </summary>
    public static void Unpremultiply(Span<byte> bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            int alpha = bgra[i + 3];
            if (alpha is 0 or 255)
                continue;
            for (var c = 0; c < 3; c++)
                bgra[i + c] = (byte)Math.Min(255, (bgra[i + c] * 255 + alpha / 2) / alpha);
        }
    }
}
