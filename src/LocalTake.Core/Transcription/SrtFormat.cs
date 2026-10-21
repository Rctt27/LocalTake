using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalTake.Core.Transcription;

/// <summary>
/// SubRip, the subtitle file every player reads next to a video of the same name. Written with Windows line endings,
/// which every player accepts; read whatever the line endings.
/// </summary>
public static partial class SrtFormat
{
    private const string NewLine = "\r\n";

    public static string Write(IEnumerable<SubtitleCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        var text = new StringBuilder();
        var number = 1;
        foreach (var cue in cues)
        {
            text.Append(number++.ToString(CultureInfo.InvariantCulture)).Append(NewLine)
                .Append(Time(cue.Start)).Append(" --> ").Append(Time(cue.End)).Append(NewLine)
                .Append(cue.Text.ReplaceLineEndings(NewLine)).Append(NewLine)
                .Append(NewLine);
        }
        return text.ToString();
    }

    /// <summary>The cues of a file, skipping blocks without a time line, as a hand-edited file may have.</summary>
    public static IReadOnlyList<SubtitleCue> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var cues = new List<SubtitleCue>();
        var lines = text.TrimStart('﻿').ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var match = TimeLine().Match(lines[i]);
            if (!match.Success)
                continue;
            var textLines = new List<string>();
            while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
                textLines.Add(lines[++i].Trim());
            cues.Add(new SubtitleCue(ReadTime(match, 1), ReadTime(match, 5), string.Join("\n", textLines)));
        }
        return cues;
    }

    private static string Time(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
    }

    /// <summary>Hours, minutes, seconds and a fraction of 1 to 3 digits ("5" is 500 ms).</summary>
    private static TimeSpan ReadTime(Match match, int group)
    {
        int Part(int offset) => int.Parse(match.Groups[group + offset].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups[group + 3].Value.PadRight(3, '0');
        return new TimeSpan(0, Part(0), Part(1), Part(2), int.Parse(fraction, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"^\s*(\d+):(\d{1,2}):(\d{1,2})[,.](\d{1,3})\s*-->\s*(\d+):(\d{1,2}):(\d{1,2})[,.](\d{1,3})")]
    private static partial Regex TimeLine();
}
