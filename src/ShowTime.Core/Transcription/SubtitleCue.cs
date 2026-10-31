namespace ShowTime.Core.Transcription;

/// <summary>One subtitle: its text, shown from <paramref name="Start"/> until just before <paramref name="End"/>.</summary>
/// <param name="Text">Lines separated by "\n", none of them blank.</param>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>A cue, or the time between two cues, in the order they come in the video.</summary>
public abstract record TimelineEntry(TimeSpan Start, TimeSpan End);

/// <param name="Index">The cue's index in <see cref="SubtitleDocument.Cues"/>.</param>
public sealed record CueEntry(int Index, SubtitleCue Cue) : TimelineEntry(Cue.Start, Cue.End);

/// <summary>Time without a subtitle, before, between or after the cues.</summary>
public sealed record GapEntry(TimeSpan Start, TimeSpan End) : TimelineEntry(Start, End);
