using ShowTime.Core.Editor;

namespace ShowTime.Core.Transcription;

/// <summary>
/// The subtitles of one video, as the user edits them: cues in order, never overlapping, each lasting at least
/// <see cref="MinDuration"/>. Every edit keeps that true or is refused, and can be undone.
/// </summary>
public sealed class SubtitleDocument
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(0.1);

    /// <summary>A cue added in a gap lasts this long, or as long as the gap when it is shorter.</summary>
    public static readonly TimeSpan NewCueDuration = TimeSpan.FromSeconds(2);

    private List<SubtitleCue> _cues;

    public SubtitleDocument(IEnumerable<SubtitleCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        _cues = Normalize(cues);
    }

    public IReadOnlyList<SubtitleCue> Cues => _cues;

    public UndoHistory History { get; } = new();

    /// <summary>Raised by every edit, undo and redo.</summary>
    public event EventHandler? Changed;

    // ---- Timeline ------------------------------------------------------------------------------------------

    /// <summary>
    /// The cues and the gaps between them, from 0 to the end of the video: every position falls in exactly one entry.
    /// Gaps of no length are left out.
    /// </summary>
    /// <param name="duration">The video's length, or zero while it is not known.</param>
    public IReadOnlyList<TimelineEntry> Timeline(TimeSpan duration)
    {
        var entries = new List<TimelineEntry>();
        var time = TimeSpan.Zero;
        for (var i = 0; i < _cues.Count; i++)
        {
            if (_cues[i].Start > time)
                entries.Add(new GapEntry(time, _cues[i].Start));
            entries.Add(new CueEntry(i, _cues[i]));
            time = _cues[i].End;
        }
        if (duration > time)
            entries.Add(new GapEntry(time, duration));
        return entries;
    }

    /// <summary>
    /// The entry shown at a position: the cue that covers it, otherwise the gap. Before 0 is the first entry, past the
    /// end the last one, so that the editor always shows something. -1 for an empty timeline.
    /// </summary>
    public static int IndexAt(IReadOnlyList<TimelineEntry> timeline, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        // The entries follow one another without overlapping: the last one starting at or before the position.
        int low = 0, high = timeline.Count - 1, found = timeline.Count > 0 ? 0 : -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (timeline[middle].Start <= position)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return found;
    }

    // ---- Editing -------------------------------------------------------------------------------------------

    /// <returns>Whether the text changed.</returns>
    public bool SetText(int index, string text)
    {
        var cleaned = CleanText(text);
        if (cleaned == _cues[index].Text)
            return false;
        Apply(cues => cues[index] = cues[index] with { Text = cleaned });
        return true;
    }

    /// <returns>Null when the start was changed, otherwise why it was refused.</returns>
    public string? SetStart(int index, TimeSpan start)
    {
        var cue = _cues[index];
        if (index > 0 && start < _cues[index - 1].End)
            return $"Le début chevauche le sous-titre précédent, qui finit à {Timecode.Format(_cues[index - 1].End)}.";
        if (start < TimeSpan.Zero || cue.End - start < MinDuration)
            return TooShort;
        if (start != cue.Start)
            Apply(cues => cues[index] = cue with { Start = start });
        return null;
    }

    /// <returns>Null when the end was changed, otherwise why it was refused.</returns>
    public string? SetEnd(int index, TimeSpan end)
    {
        var cue = _cues[index];
        if (index + 1 < _cues.Count && end > _cues[index + 1].Start)
            return $"La fin chevauche le sous-titre suivant, qui commence à {Timecode.Format(_cues[index + 1].Start)}.";
        if (end - cue.Start < MinDuration)
            return TooShort;
        if (end != cue.End)
            Apply(cues => cues[index] = cue with { End = end });
        return null;
    }

    public void Remove(int index) => Apply(cues => cues.RemoveAt(index));

    /// <summary>Adds an empty cue at the start of a gap.</summary>
    /// <returns>The new cue's index, or -1 when the gap is too short or overlaps a cue.</returns>
    public int AddInGap(GapEntry gap)
    {
        ArgumentNullException.ThrowIfNull(gap);
        var end = gap.End - gap.Start > NewCueDuration ? gap.Start + NewCueDuration : gap.End;
        var index = _cues.FindIndex(c => c.Start >= gap.Start);
        if (index < 0)
            index = _cues.Count;
        if (end - gap.Start < MinDuration || (index > 0 && _cues[index - 1].End > gap.Start) ||
            (index < _cues.Count && _cues[index].Start < end))
            return -1;
        Apply(cues => cues.Insert(index, new SubtitleCue(gap.Start, end, "")));
        return index;
    }

    private const string TooShort = "Un sous-titre dure au moins 0,1 s.";

    /// <summary>Applies an edit to a copy of the cues, so that undo and redo swap whole lists.</summary>
    private void Apply(Action<List<SubtitleCue>> edit)
    {
        var before = _cues;
        var after = new List<SubtitleCue>(_cues);
        edit(after);
        Replace(after);
        History.Push(() => Replace(before), () => Replace(after));
    }

    private void Replace(List<SubtitleCue> cues)
    {
        _cues = cues;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Text and times ------------------------------------------------------------------------------------

    /// <summary>Trimmed lines without blank ones: in SRT, a blank line ends the cue.</summary>
    private static string CleanText(string text) =>
        string.Join("\n", (text ?? "").ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    /// <summary>In order; a cue starting before the previous one ends starts at that end.</summary>
    private static List<SubtitleCue> Normalize(IEnumerable<SubtitleCue> cues)
    {
        var result = new List<SubtitleCue>();
        var time = TimeSpan.Zero;
        foreach (var cue in cues.OrderBy(c => c.Start))
        {
            var start = cue.Start > time ? cue.Start : time;
            var end = cue.End - start >= MinDuration ? cue.End : start + MinDuration;
            result.Add(cue with { Start = start, End = end });
            time = end;
        }
        return result;
    }
}
