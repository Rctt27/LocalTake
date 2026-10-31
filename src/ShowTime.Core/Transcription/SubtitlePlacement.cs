namespace ShowTime.Core.Transcription;

/// <summary>
/// Where the player shows the current subtitle: near the bottom of the picture, inside the black bars the player may
/// leave around it, with a font in proportion to the picture: about 4.5 % of its height, as on a television. In the
/// view's own units (WPF's device-independent pixels, or points).
/// </summary>
public sealed record SubtitlePlacement(double FontSize, double MaxWidth, double BottomMargin)
{
    /// <param name="videoWidth">The video's size in pixels, or 0 while the player does not know it: it then fills the host.</param>
    public static SubtitlePlacement For(double hostWidth, double hostHeight, double videoWidth, double videoHeight)
    {
        double pictureWidth = hostWidth, pictureHeight = hostHeight;
        if (videoWidth > 0 && videoHeight > 0)
        {
            var scale = Math.Min(hostWidth / videoWidth, hostHeight / videoHeight);
            pictureWidth = videoWidth * scale;
            pictureHeight = videoHeight * scale;
        }
        return new SubtitlePlacement(Math.Max(12, pictureHeight * 0.045), pictureWidth * 0.9,
            (hostHeight - pictureHeight) / 2 + pictureHeight * 0.05);
    }
}
