using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;

namespace LocalTake.Infrastructure.Recording;

/// <summary>Records the screen with this platform's ffmpeg inputs.</summary>
public sealed class FfmpegVideoRecorderFactory(IFfmpegPlatform platform) : IVideoRecorderFactory
{
    public IVideoRecorder Start(string ffmpegPath, PixelRect area, string outputPath, VideoOptions options, Action<IVideoRecorder> onUnexpectedExit) =>
        FfmpegVideoRecorder.Start(ffmpegPath, platform.BuildInputs(area, options, null), outputPath, onUnexpectedExit);
}
