using System.Globalization;
using System.Text.RegularExpressions;
using LocalTake.Core.Geometry;

namespace LocalTake.Core.Recording;

/// <summary>
/// macOS: AVFoundation for the screen and the microphone; the Mac's sound comes from LocalTake's own capture
/// (ScreenCaptureKit) through a pipe, as on Windows. AVFoundation records a whole display, so the area is cut out of
/// it with a crop filter; an area across two displays keeps the part on the display holding most of it.
/// </summary>
/// <param name="displays">The displays in AVFoundation's order ("Capture screen 0", 1…), in physical pixels.</param>
public sealed partial class AvFoundation(Func<IReadOnlyList<PixelRect>> displays) : IFfmpegPlatform
{
    /// <summary>
    /// The virtual audio driver LocalTake once read the Mac's sound from: no longer recorded, only kept out of the
    /// microphones when it is installed.
    /// </summary>
    public string SystemAudioDevice => "BlackHole 2ch";

    public string SystemAudioRequirement => "l'autorisation Enregistrement de l'écran";

    public string InstallHint => "Il est livré dans LocalTake.app : réinstallez l'app depuis la page des versions, " +
                                 "ou indiquez un autre ffmpeg dans les réglages.";

    public IReadOnlyList<string> ListDevicesArguments { get; } = ["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""];

    public IReadOnlyList<string> ParseDeviceList(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var devices = new List<string>();
        var inAudioSection = false;
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("AVFoundation audio devices", StringComparison.Ordinal))
                inAudioSection = true;
            else if (line.Contains("AVFoundation video devices", StringComparison.Ordinal))
                inAudioSection = false;
            else if (inAudioSection && DeviceLine().Match(line) is { Success: true } match)
                devices.Add(match.Groups[1].Value.TrimEnd('\r'));
        }
        return devices;
    }

    /// <summary>Always: ScreenCaptureKit captures the Mac's sound on every macOS LocalTake runs on.</summary>
    public bool CanRecordSystemAudio(IReadOnlyCollection<string> devices) => true;

    public CaptureInputs BuildInputs(PixelRect area, VideoOptions options, PipedAudio? systemAudio)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.SystemAudio && systemAudio is null)
            throw new ArgumentException("The computer sound is read from a pipe.", nameof(systemAudio));
        var piped = options.SystemAudio ? systemAudio : null;

        var audio = new List<IReadOnlyList<string>>();
        if (!string.IsNullOrEmpty(options.Microphone))
            audio.Add(AudioInput(options.Microphone));

        var all = displays();
        var index = 0;
        long best = -1;
        for (var i = 0; i < all.Count; i++)
        {
            var common = all[i].Intersect(area);
            var overlap = (long)common.Width * common.Height;
            if (overlap > best)
                (best, index) = (overlap, i);
        }
        var display = all.Count > 0 ? all[index] : area;
        var crop = (all.Count > 0 ? display.Intersect(area) : area).WithEvenSize();
        var culture = CultureInfo.InvariantCulture;
        // As with gdigrab on Windows: probing the screen would delay the opening of the pipe, whose capture only starts
        // then, and the sound would come out ahead of the picture.
        string[] video =
        [
            "-f", "avfoundation", "-thread_queue_size", "1024", .. piped is null ? [] : NoProbe, "-capture_cursor", "1",
            "-framerate", options.FrameRate.ToString(culture), "-i", $"Capture screen {index.ToString(culture)}:none"
        ];
        var filter = string.Create(culture, $"crop={crop.Width}:{crop.Height}:{crop.X - display.X}:{crop.Y - display.Y}");
        if (piped is not { Format: var format })
            return new CaptureInputs(audio, video, filter);
        // After the screen, so that the sound starts with the first frame; its format is declared, nothing to probe.
        string[] pipe =
        [
            "-f", format.SampleFormat, "-ar", format.SampleRate.ToString(culture), "-ac", format.Channels.ToString(culture),
            "-thread_queue_size", "1024", .. NoProbe, "-i", piped.PipePath
        ];
        return new CaptureInputs(audio, video, filter, TrailingAudioInputs: [pipe]);
    }

    private static readonly string[] NoProbe = ["-probesize", "32", "-analyzeduration", "0"];

    private static string[] AudioInput(string device) => ["-f", "avfoundation", "-thread_queue_size", "1024", "-i", ":" + device];

    [GeneratedRegex(@"\] \[\d+\] (.+)$")]
    private static partial Regex DeviceLine();
}
