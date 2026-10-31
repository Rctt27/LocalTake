using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;
using ShowTime.Core.Recording;

namespace ShowTime.Infrastructure.Recording;

/// <summary>Records the screen with this platform's ffmpeg inputs.</summary>
/// <param name="systemAudio">The computer sound, on a platform where ShowTime captures it itself (Windows).</param>
public sealed class FfmpegVideoRecorderFactory(IFfmpegPlatform platform, ISystemAudioSource? systemAudio = null) : IVideoRecorderFactory
{
    public IVideoRecorder Start(string ffmpegPath, PixelRect area, string outputPath, VideoOptions options, Action<IVideoRecorder> onUnexpectedExit)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Before ffmpeg starts: the pipe must be waiting when ffmpeg opens it.
        var capture = options.SystemAudio ? systemAudio?.Start() : null;
        try
        {
            return FfmpegVideoRecorder.Start(ffmpegPath, platform.BuildInputs(area, options, capture?.Input), outputPath,
                onUnexpectedExit, capture, options.MicrophoneTrackPath);
        }
        catch
        {
            capture?.Dispose();
            throw;
        }
    }
}
