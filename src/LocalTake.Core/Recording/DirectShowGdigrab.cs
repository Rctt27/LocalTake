using System.Globalization;
using System.Text.RegularExpressions;
using LocalTake.Core.Geometry;

namespace LocalTake.Core.Recording;

/// <summary>Windows: DirectShow for the sound, gdigrab for the screen.</summary>
public sealed partial class DirectShowGdigrab : IFfmpegPlatform
{
    /// <summary>Audio driver installed with TinyTake ("TinyTake Filter"): it records what the computer plays.</summary>
    public string SystemAudioDevice => "virtual-audio-capturer";

    public string InstallHint => "Installez-le avec la commande « winget install Gyan.FFmpeg », " +
                                 "ou indiquez l'emplacement de ffmpeg.exe dans les réglages.";

    public IReadOnlyList<string> ListDevicesArguments { get; } =
        ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"];

    /// <summary>
    /// Recent builds tag each device: [dshow @ …] "Name" (audio). Older ones list devices under a "DirectShow audio
    /// devices" heading, each followed by an "Alternative name" line.
    /// </summary>
    public IReadOnlyList<string> ParseDeviceList(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var devices = new List<string>();
        var inAudioSection = false;
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("DirectShow audio devices", StringComparison.Ordinal))
                inAudioSection = true;
            else if (line.Contains("DirectShow video devices", StringComparison.Ordinal))
                inAudioSection = false;

            var match = DeviceLine().Match(line);
            if (!match.Success || line.Contains("Alternative name", StringComparison.Ordinal))
                continue;
            var kind = match.Groups[3].Value;
            if (kind == "audio" || (kind.Length == 0 && inAudioSection))
                devices.Add(match.Groups[1].Value);
        }
        return devices.Distinct().ToList();
    }

    public CaptureInputs BuildInputs(PixelRect area, VideoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var audio = new List<IReadOnlyList<string>>();
        if (!string.IsNullOrEmpty(options.Microphone))
            audio.Add(AudioInput(options.Microphone));
        if (options.SystemAudio)
            audio.Add(AudioInput(SystemAudioDevice));

        // gdigrab takes physical coordinates on the virtual desktop, like the rest of LocalTake.
        string[] video =
        [
            "-f", "gdigrab", "-thread_queue_size", "1024", "-framerate", Invariant(options.FrameRate),
            "-offset_x", Invariant(area.X), "-offset_y", Invariant(area.Y),
            "-video_size", $"{Invariant(area.Width)}x{Invariant(area.Height)}", "-draw_mouse", "1", "-i", "desktop"
        ];
        return new CaptureInputs(audio, video);
    }

    private static string[] AudioInput(string device) =>
        ["-f", "dshow", "-thread_queue_size", "1024", "-audio_buffer_size", "50", "-i", "audio=" + device];

    /// <summary>Some cultures write negative numbers with a Unicode minus sign, which ffmpeg rejects.</summary>
    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex("\"([^\"]+)\"\\s*(\\((audio|video|none)\\))?")]
    private static partial Regex DeviceLine();
}
