using LocalTake.Core.Geometry;

namespace LocalTake.Core.Recording;

/// <summary>How ffmpeg captures the screen and the sound on one operating system.</summary>
public interface IFfmpegPlatform
{
    /// <summary>The audio input that records what the computer plays.</summary>
    string SystemAudioDevice { get; }

    /// <summary>How to install ffmpeg, shown when it cannot be found.</summary>
    string InstallHint { get; }

    /// <summary>Arguments that make ffmpeg list the audio inputs on its standard error.</summary>
    IReadOnlyList<string> ListDevicesArguments { get; }

    /// <summary>Audio input names, as <see cref="BuildInputs"/> expects them.</summary>
    IReadOnlyList<string> ParseDeviceList(string output);

    /// <param name="area">Physical screen pixels, with even dimensions.</param>
    CaptureInputs BuildInputs(PixelRect area, VideoOptions options);
}
