using System.Globalization;

namespace ShowTime.Core.Transcription;

/// <summary>One word as Whisper heard it, with when it was said.</summary>
public sealed record TranscribedWord(TimeSpan Start, TimeSpan End, string Text);

public enum SubtitleWarningLevel
{
    /// <summary>Readable, but past the comfortable limit.</summary>
    Notice,

    /// <summary>Past what professional subtitles allow.</summary>
    Problem
}

public sealed record SubtitleWarning(SubtitleWarningLevel Level, string Message);

/// <summary>
/// How subtitles are cut and what makes them readable. Every value of a profile is here, so that it can be tuned in one
/// place.
/// </summary>
/// <param name="TargetLineLength">
/// Characters per line the segmenter aims for: past it, a line costs more, so that cues hold a group of words rather
/// than fill the line.
/// </param>
/// <param name="MaxLineLength">Characters per line: 42, Netflix's limit for French and most Latin-script languages.</param>
/// <param name="MaxLines">Lines per cue. The segmenter only takes the second when one line would make cues too short.</param>
/// <param name="MinDuration">A cue shorter than this flashes by: Netflix's floor is 5/6 s.</param>
/// <param name="MaxDuration">Netflix allows 7 s; in a tutorial, the eye goes back to the screen sooner.</param>
/// <param name="PauseSplit">A silence at least this long between two words ends the cue.</param>
/// <param name="Linger">How long a cue may stay after its last word, when nothing follows at once.</param>
/// <param name="MinGap">Left between a lingering cue and the next one, so that the change is seen.</param>
/// <param name="ComfortableReadingSpeed">Characters per second: Arcom's charter asks for 12 to 15.</param>
/// <param name="MaxReadingSpeed">Characters per second: Netflix's limit for French adult programmes.</param>
public sealed record SubtitleProfile(
    int TargetLineLength, int MaxLineLength, int MaxLines, TimeSpan MinDuration, TimeSpan MaxDuration, TimeSpan PauseSplit, TimeSpan Linger,
    TimeSpan MinGap, double ComfortableReadingSpeed, double MaxReadingSpeed)
{
    /// <summary>
    /// Screen recordings: one line wherever possible, so that the subtitle hides little of what is shown, and short
    /// cues, each a group of words that belong together: about 32 characters, five or six words.
    /// </summary>
    public static SubtitleProfile Tutorial { get; } = new(
        TargetLineLength: 32, MaxLineLength: 42, MaxLines: 2, MinDuration: TimeSpan.FromSeconds(1), MaxDuration: TimeSpan.FromSeconds(5),
        PauseSplit: TimeSpan.FromSeconds(0.5), Linger: TimeSpan.FromSeconds(0.5), MinGap: TimeSpan.FromSeconds(0.1),
        ComfortableReadingSpeed: 15, MaxReadingSpeed: 17);

    /// <summary>Characters shown per second, line breaks left out; 0 for a cue without duration.</summary>
    public static double ReadingSpeed(SubtitleCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        var seconds = (cue.End - cue.Start).TotalSeconds;
        return seconds <= 0 ? 0 : cue.Text.Replace("\n", "", StringComparison.Ordinal).Length / seconds;
    }

    /// <summary>What makes a cue hard to read, as the editor shows it; empty when it fits the profile.</summary>
    public IReadOnlyList<SubtitleWarning> Check(SubtitleCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        var warnings = new List<SubtitleWarning>();
        var lines = cue.Text.Split('\n');
        if (lines.Any(l => l.Length > MaxLineLength))
            warnings.Add(new SubtitleWarning(SubtitleWarningLevel.Problem, $"Une ligne dépasse {MaxLineLength} caractères."));
        if (lines.Length > MaxLines)
            warnings.Add(new SubtitleWarning(SubtitleWarningLevel.Problem, $"Plus de {MaxLines} lignes."));

        var speed = ReadingSpeed(cue);
        var shown = Math.Round(speed).ToString(CultureInfo.InvariantCulture);
        if (speed > MaxReadingSpeed)
        {
            warnings.Add(new SubtitleWarning(SubtitleWarningLevel.Problem,
                $"{shown} caractères par seconde : trop rapide à lire ({MaxReadingSpeed} au plus)."));
        }
        else if (speed > ComfortableReadingSpeed)
        {
            warnings.Add(new SubtitleWarning(SubtitleWarningLevel.Notice,
                $"{shown} caractères par seconde : un peu rapide à lire ({ComfortableReadingSpeed} au plus, idéalement)."));
        }
        return warnings;
    }
}
