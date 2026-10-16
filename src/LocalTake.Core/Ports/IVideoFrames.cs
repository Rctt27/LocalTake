using LocalTake.Core.Recording;

namespace LocalTake.Core.Ports;

/// <summary>Reads a saved video, for its preview.</summary>
public interface IVideoFrames
{
    /// <returns>Null when the file cannot be read.</returns>
    Task<VideoInfo?> ProbeAsync(string ffmpegPath, string videoPath);

    /// <summary>The frame shown at a time, scaled to a width (the height keeps the proportions).</summary>
    /// <returns>Null when no frame could be decoded.</returns>
    Task<PixelBuffer?> FrameAsync(string ffmpegPath, string videoPath, TimeSpan at, int width, VideoInfo info, CancellationToken cancellation = default);
}
