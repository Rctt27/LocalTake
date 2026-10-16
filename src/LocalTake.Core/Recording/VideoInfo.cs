using System.Globalization;
using System.Text.RegularExpressions;

namespace LocalTake.Core.Recording;

/// <summary>What ffmpeg says about a video file.</summary>
public sealed partial record VideoInfo(TimeSpan Duration, int Width, int Height, bool HasAudio)
{
    /// <summary>Reads the output of "ffmpeg -i file".</summary>
    /// <returns>Null when ffmpeg found no video stream.</returns>
    public static VideoInfo? Parse(string ffmpegOutput)
    {
        ArgumentNullException.ThrowIfNull(ffmpegOutput);
        var video = VideoStream().Match(ffmpegOutput);
        if (!video.Success)
            return null;

        var duration = TimeSpan.Zero;
        var time = DurationLine().Match(ffmpegOutput);
        if (time.Success)
        {
            var culture = CultureInfo.InvariantCulture;
            duration = TimeSpan.FromHours(int.Parse(time.Groups[1].Value, culture))
                       + TimeSpan.FromMinutes(int.Parse(time.Groups[2].Value, culture))
                       + TimeSpan.FromSeconds(double.Parse(time.Groups[3].Value, culture));
        }
        return new VideoInfo(
            duration,
            int.Parse(video.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(video.Groups[2].Value, CultureInfo.InvariantCulture),
            ffmpegOutput.Contains("Audio:", StringComparison.Ordinal));
    }

    public static string Format(TimeSpan time) =>
        ((int)time.TotalHours).ToString("00", CultureInfo.InvariantCulture) + time.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationLine();

    [GeneratedRegex(@"Stream #.*Video:.*?\b(\d{2,5})x(\d{2,5})\b")]
    private static partial Regex VideoStream();
}
