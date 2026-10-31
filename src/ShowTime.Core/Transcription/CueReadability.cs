using System.Globalization;

namespace ShowTime.Core.Transcription;

public enum ReadabilityLevel
{
    Easy,
    Notice,
    Problem
}

/// <summary>
/// How easily a cue reads, as the subtitle panel shows it beside its times: the reading speed, coloured by its level,
/// with the reasons in its tooltip. Checked against the tutorial profile, which cut the transcription.
/// </summary>
public sealed record CueReadability(string Speed, ReadabilityLevel Level, string ToolTip)
{
    public static CueReadability Of(SubtitleCue cue)
    {
        var warnings = SubtitleProfile.Tutorial.Check(cue);
        var speed = Math.Round(SubtitleProfile.ReadingSpeed(cue)).ToString(CultureInfo.CurrentCulture) + " car./s";
        var level = warnings.Any(w => w.Level == SubtitleWarningLevel.Problem) ? ReadabilityLevel.Problem
            : warnings.Count > 0 ? ReadabilityLevel.Notice : ReadabilityLevel.Easy;
        var toolTip = warnings.Count > 0
            ? string.Join(Environment.NewLine, warnings.Select(w => w.Message))
            : "Caractères affichés par seconde : facile à lire (15 au plus, idéalement).";
        return new CueReadability(speed, level, toolTip);
    }
}
