using System.Globalization;
using System.Text.RegularExpressions;
using LocalTake.Core.Geometry;

namespace LocalTake.Core.Recording;

/// <summary>
/// macOS: AVFoundation for the screen and the sound. AVFoundation records a whole display, so the area is cut out of
/// it with a crop filter; an area across two displays keeps the part on the display holding most of it.
/// </summary>
/// <param name="displays">The displays in AVFoundation's order ("Capture screen 0", 1…), in physical pixels.</param>
public sealed partial class AvFoundation(Func<IReadOnlyList<PixelRect>> displays) : IFfmpegPlatform
{
    /// <summary>The free virtual audio driver that records what the Mac plays.</summary>
    public string SystemAudioDevice => "BlackHole 2ch";

    public string SystemAudioRequirement => "le pilote audio « BlackHole 2ch » (brew install blackhole-2ch)";

    public string InstallHint => "Installez-le avec la commande « brew install ffmpeg », " +
                                 "ou indiquez l'emplacement de ffmpeg dans les réglages.";

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

    /// <summary>BlackHole, when listed; tried anyway when ffmpeg could not list the devices.</summary>
    public bool CanRecordSystemAudio(IReadOnlyCollection<string> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices.Count == 0 || devices.Contains(SystemAudioDevice);
    }

    /// <param name="systemAudio">Unused: the Mac records the computer sound through BlackHole.</param>
    public CaptureInputs BuildInputs(PixelRect area, VideoOptions options, PipedAudio? systemAudio)
    {
        ArgumentNullException.ThrowIfNull(options);
        var audio = new List<IReadOnlyList<string>>();
        if (!string.IsNullOrEmpty(options.Microphone))
            audio.Add(AudioInput(options.Microphone));
        if (options.SystemAudio)
            audio.Add(AudioInput(SystemAudioDevice));

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
        string[] video =
        [
            "-f", "avfoundation", "-thread_queue_size", "1024", "-capture_cursor", "1",
            "-framerate", options.FrameRate.ToString(culture), "-i", $"Capture screen {index.ToString(culture)}:none"
        ];
        var filter = string.Create(culture, $"crop={crop.Width}:{crop.Height}:{crop.X - display.X}:{crop.Y - display.Y}");
        return new CaptureInputs(audio, video, filter);
    }

    private static string[] AudioInput(string device) => ["-f", "avfoundation", "-thread_queue_size", "1024", "-i", ":" + device];

    [GeneratedRegex(@"\] \[\d+\] (.+)$")]
    private static partial Regex DeviceLine();
}
