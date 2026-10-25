using System.Globalization;
using LocalTake.Core.Ports;

namespace LocalTake.Core.Transcription;

/// <summary>What the subtitle panel says above its rows.</summary>
/// <param name="IsError">The subtitles could not be saved: the text says so on a second line.</param>
/// <param name="Progress">The transcription's progress, from 0 to 1, shown while <paramref name="ShowsProgress"/>.</param>
/// <param name="CanRetry">The transcription failed: "Réessayer" is offered.</param>
public sealed record SubtitleStatus(string Text, bool IsError, bool ShowsProgress, double Progress, bool CanRetry);

/// <summary>
/// The rules of the subtitle panel beside the video, without its views: the transcription it follows or the .srt it
/// loaded, the timeline of cues and gaps, the entry at the playing position, edits checked by
/// <see cref="SubtitleDocument"/>, and each change saved to the .srt shortly after. Each app draws the rows.
/// </summary>
public sealed class SubtitleEditor(SubtitleFile file, Transcriptions transcriptions, TimeProvider clock, IUiThread uiThread)
{
    /// <summary>How long after the last change the .srt is written.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private ITimer? _saveTimer;
    private bool _savePending;
    private bool _closed;
    private string _videoPath = "";
    private TranscriptionJob? _job;
    private SubtitleDocument? _document;
    private TimeSpan _duration;
    private TimeSpan _position;
    private string? _saveError;

    /// <summary>The rows must be rebuilt from <see cref="Timeline"/>; true when the active row should be centred.</summary>
    public event Action<bool>? TimelineChanged;

    /// <summary><see cref="ActiveIndex"/> changed; true when its row should be centred.</summary>
    public event Action<bool>? ActiveChanged;

    /// <summary><see cref="Status"/>, <see cref="CanGenerate"/> or <see cref="HasSubtitles"/> may have changed.</summary>
    public event Action? StatusChanged;

    /// <summary>The text to show over the video changed: another cue, an edited text, or a gap (null).</summary>
    public event Action<string?>? CueShown;

    /// <summary>Whether there is anything to show: a transcription, or subtitles saved earlier.</summary>
    public bool HasSubtitles => _job is not null || _document is not null;

    /// <summary>
    /// Whether the video has no subtitles to show and none on the way: "Générer les sous-titres" is offered then. After a
    /// failure, "Réessayer" is.
    /// </summary>
    public bool CanGenerate =>
        _job?.State is null or TranscriptionState.Done or TranscriptionState.Canceled && (_document?.Cues.Count ?? 0) == 0;

    public IReadOnlyList<SubtitleCue> Cues => _document?.Cues ?? [];

    public IReadOnlyList<TimelineEntry> Timeline { get; private set; } = [];

    /// <summary>The entry of <see cref="Timeline"/> at the playing position, or -1.</summary>
    public int ActiveIndex { get; private set; } = -1;

    /// <summary>The text of the cue at the playing position; null in a gap or on a cue still without text.</summary>
    public string? CueText { get; private set; }

    public SubtitleStatus Status => new(StatusText(), _document is not null && _saveError is not null,
        _job?.State == TranscriptionState.Running, _job?.Progress ?? 0, _job?.State == TranscriptionState.Failed);

    /// <param name="job">The transcription under way, or null to show the .srt already next to the video, if any.</param>
    public void Load(string videoPath, TranscriptionJob? job)
    {
        _videoPath = videoPath;
        if (job is not null)
        {
            Attach(job);
            return;
        }
        try
        {
            // An empty file is no subtitles: the player offers to generate them.
            if (file.Load(videoPath) is { Cues.Count: > 0 } saved)
                SetDocument(saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable subtitles: the video plays without them.
        }
    }

    /// <summary>"Générer les sous-titres": transcribes the video, from its microphone track when it was kept.</summary>
    /// <param name="microphoneTrack">The track recorded with the video, or null to transcribe the video's own sound.</param>
    public void Generate(string? microphoneTrack) => Attach(transcriptions.Enqueue(_videoPath, microphoneTrack));

    /// <summary>"Réessayer", after a failure.</summary>
    public void Retry()
    {
        if (_job is not null)
            transcriptions.Retry(_job);
    }

    /// <summary>The panel closes; the editor is done with.</summary>
    /// <param name="discard">The video is deleted: its transcription stops and nothing more is saved.</param>
    public void Close(bool discard)
    {
        if (_job is not null)
        {
            _job.Changed -= OnJobChanged;
            if (discard)
                transcriptions.Cancel(_job);
        }
        if (discard)
            _savePending = false;
        else if (_savePending)
            Save();
        if (_document is not null)
            _document.Changed -= OnDocumentChanged;
        _closed = true;
        _saveTimer?.Dispose();
        _saveTimer = null;
    }

    /// <summary>The video's length, once the player knows it: the last gap runs to it.</summary>
    public void SetDuration(TimeSpan duration)
    {
        _duration = duration;
        Sync(center: true);
    }

    /// <summary>The playing position: its cue, or the gap it falls in, becomes the active entry.</summary>
    public void ShowPosition(TimeSpan position)
    {
        _position = position;
        UpdateActive(center: true);
    }

    /// <returns>Null when the start was applied or is unchanged; otherwise why it was refused.</returns>
    public string? CommitStart(int cueIndex, string text) => CommitTime(cueIndex, text, c => c.Start, (d, t) => d.SetStart(cueIndex, t));

    /// <returns>Null when the end was applied or is unchanged; otherwise why it was refused.</returns>
    public string? CommitEnd(int cueIndex, string text) => CommitTime(cueIndex, text, c => c.End, (d, t) => d.SetEnd(cueIndex, t));

    /// <returns>False when the text was refused: the view shows the cue's own again.</returns>
    public bool CommitText(int cueIndex, string text) =>
        _document is null || cueIndex >= _document.Cues.Count || _document.SetText(cueIndex, text);

    public void Remove(int cueIndex) => _document?.Remove(cueIndex);

    /// <returns>The new cue's index, or -1 when the gap has no room for one.</returns>
    public int AddInGap(GapEntry gap) => _document?.AddInGap(gap) ?? -1;

    public bool Undo() => _document?.History.Undo() == true;

    public bool Redo() => _document?.History.Redo() == true;

    private string? CommitTime(int cueIndex, string text, Func<SubtitleCue, TimeSpan> current, Func<SubtitleDocument, TimeSpan, string?> apply)
    {
        if (_document is null || cueIndex >= _document.Cues.Count)
            return null;
        if (!Timecode.TryParse(text, out var time))
            return "Temps illisible : écrivez-le par exemple 01:02.345.";
        return time == current(_document.Cues[cueIndex]) ? null : apply(_document, time);
    }

    // ---- Transcription and document ------------------------------------------------------------------------

    /// <summary>Follows a transcription; subtitles without any cue, from an earlier one, make way for its result.</summary>
    private void Attach(TranscriptionJob job)
    {
        if (_job is not null)
            _job.Changed -= OnJobChanged;
        if (_document is { Cues.Count: 0 } empty)
        {
            empty.Changed -= OnDocumentChanged;
            _document = null;
            Timeline = [];
            ActiveIndex = -1;
            TimelineChanged?.Invoke(false);
            ShowCue();
        }
        _job = job;
        job.Changed += OnJobChanged;
        OnJobChanged(job, EventArgs.Empty);
    }

    private void OnJobChanged(object? sender, EventArgs e)
    {
        if (_job is { State: TranscriptionState.Done } job && _document is null)
            SetDocument(new SubtitleDocument(job.Cues));
        StatusChanged?.Invoke();
    }

    private void SetDocument(SubtitleDocument document)
    {
        _document = document;
        document.Changed += OnDocumentChanged;
        Sync(center: true);
        StatusChanged?.Invoke();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        Sync(center: false);
        ScheduleSave();
        StatusChanged?.Invoke();
    }

    private string StatusText()
    {
        if (_document is not null)
        {
            var count = _document.Cues.Count switch
            {
                0 => "Aucune parole détectée. Ajoutez des sous-titres avec « Ajouter » dans la liste.",
                1 => "1 sous-titre",
                var n => $"{n} sous-titres"
            };
            return _saveError is null ? count : count + "\n" + _saveError;
        }
        return _job?.State switch
        {
            TranscriptionState.Waiting => "Transcription en attente : elle commence après la transcription ou l'enregistrement en cours.",
            TranscriptionState.Running => "Transcription du micro… " + _job.Progress.ToString("P0", CultureInfo.CurrentCulture),
            TranscriptionState.Failed => _job.Error ?? "",
            TranscriptionState.Canceled => "Transcription annulée.",
            _ => ""
        };
    }

    // ---- Saving --------------------------------------------------------------------------------------------

    private void ScheduleSave()
    {
        if (_closed)
            return;
        _savePending = true;
        _saveTimer ??= clock.CreateTimer(_ => uiThread.Post(() =>
        {
            if (_savePending && !_closed)
                Save();
        }), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void Save()
    {
        _savePending = false;
        if (_document is null)
            return;
        try
        {
            file.Save(_videoPath, _document.Cues);
            _saveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _saveError = "Les sous-titres n'ont pas pu être enregistrés : " + ex.Message;
        }
        StatusChanged?.Invoke();
    }

    // ---- Following the video -------------------------------------------------------------------------------

    private void Sync(bool center)
    {
        if (_document is null)
            return;
        Timeline = _document.Timeline(_duration);
        ActiveIndex = -1;
        TimelineChanged?.Invoke(center);
        UpdateActive(center);
    }

    private void UpdateActive(bool center)
    {
        var index = SubtitleDocument.IndexAt(Timeline, _position);
        if (index != ActiveIndex)
        {
            ActiveIndex = index;
            if (index >= 0)
                ActiveChanged?.Invoke(center);
        }
        ShowCue();
    }

    private void ShowCue()
    {
        var text = ActiveIndex >= 0 && ActiveIndex < Timeline.Count && Timeline[ActiveIndex] is CueEntry { Cue.Text: { Length: > 0 } cueText }
            ? cueText
            : null;
        if (text == CueText)
            return;
        CueText = text;
        CueShown?.Invoke(text);
    }
}
