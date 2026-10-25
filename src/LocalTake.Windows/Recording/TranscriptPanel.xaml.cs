using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalTake.Core.Transcription;

namespace LocalTake;

/// <summary>
/// The subtitles beside the video, cue by cue with the gaps between them, text and times editable. The row at the
/// playing position is highlighted and kept in the middle, so that the panel always shows the place being played: a
/// cue, or the gap between two. Every change is saved to the .srt shortly after.
/// </summary>
public partial class TranscriptPanel : UserControl
{
    internal static readonly Brush ActiveBackground = Frozen("#2D3440");
    private static readonly Brush ActiveBorder = Frozen("#0A64AD");
    private static readonly Brush FieldBackground = Frozen("#1B1B1C");
    private static readonly Brush FieldBorder = Frozen("#3C3C3C");
    private static readonly Brush ErrorBrush = Frozen("#F28B82");
    private static readonly Brush TextBrush = Frozen("#EEEEEE");
    private static readonly Brush DimBrush = Frozen("#8A8A8A");
    private static readonly Brush NoticeBrush = Frozen("#F2C14E");

    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string _videoPath = "";
    private SubtitleFile? _file;
    private Transcriptions? _transcriptions;
    private TranscriptionJob? _job;
    private SubtitleDocument? _document;
    private TimeSpan _duration;
    private TimeSpan _position;
    private IReadOnlyList<TimelineEntry> _timeline = [];
    private int _active = -1;
    private string? _saveError;
    private string? _shownCue;
    private bool _centerPending;

    /// <summary>Set while a click in the panel moves the video: that row is under the mouse, it is not scrolled away.</summary>
    private bool _seekingFromPanel;

    public TranscriptPanel()
    {
        InitializeComponent();
        _saveTimer.Tick += (_, _) => Save();
    }

    /// <summary>A row clicked or a field entered: the video goes to that place.</summary>
    public event Action<TimeSpan>? SeekRequested;

    /// <summary>A field is being edited: the video pauses, so that the rows stay still under the cursor.</summary>
    public event Action? EditingStarted;

    /// <summary>The text of the cue at the playing position, for the player to show over the video; null in a gap.</summary>
    public event Action<string?>? CueShown;

    /// <summary>A transcription started or ended, or cues were added or removed: <see cref="CanGenerate"/> may change.</summary>
    public event Action? SubtitlesChanged;

    /// <summary>Whether there is anything to show: a transcription, or subtitles saved earlier.</summary>
    internal bool HasSubtitles => _job is not null || _document is not null;

    /// <summary>
    /// Whether the video has no subtitles to show and none on the way: "Générer les sous-titres" is offered then. After a
    /// failure, the panel's "Réessayer" is.
    /// </summary>
    internal bool CanGenerate =>
        _job?.State is null or TranscriptionState.Done or TranscriptionState.Canceled && (_document?.Cues.Count ?? 0) == 0;

    /// <summary>Whether a field of the panel has the keyboard: keys then type text instead of driving the player.</summary>
    internal bool IsEditing => Keyboard.FocusedElement is TextBox box && IsAncestorOf(box);

    /// <summary>The subtitles as they stand, for the tests and the owner.</summary>
    internal IReadOnlyList<SubtitleCue> Cues => _document?.Cues ?? [];

    /// <param name="job">The transcription under way, or null to show the .srt already next to the video, if any.</param>
    internal void Load(string videoPath, SubtitleFile file, Transcriptions transcriptions, TranscriptionJob? job)
    {
        _videoPath = videoPath;
        _file = file;
        _transcriptions = transcriptions;
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
    internal void Generate(string? microphoneTrack)
    {
        if (_transcriptions is null)
            return;
        Attach(_transcriptions.Enqueue(_videoPath, microphoneTrack));
    }

    /// <summary>Follows a transcription; subtitles without any cue, from an earlier one, make way for its result.</summary>
    private void Attach(TranscriptionJob job)
    {
        if (_job is not null)
            _job.Changed -= OnJobChanged;
        if (_document is { Cues.Count: 0 } empty)
        {
            empty.Changed -= OnDocumentChanged;
            _document = null;
            _timeline = [];
            _active = -1;
            Rows.Children.Clear();
            ShowCue();
        }
        _job = job;
        job.Changed += OnJobChanged;
        OnJobChanged(job, EventArgs.Empty);
    }

    /// <param name="discard">The video is deleted: its transcription stops and nothing more is saved.</param>
    internal void Close(bool discard)
    {
        if (_job is not null)
        {
            _job.Changed -= OnJobChanged;
            if (discard)
                _transcriptions?.Cancel(_job);
        }
        if (discard)
            _saveTimer.Stop();
        else if (_saveTimer.IsEnabled)
            Save();
        if (_document is not null)
            _document.Changed -= OnDocumentChanged;
    }

    /// <summary>The video's length, once the player knows it: the last gap runs to it.</summary>
    internal void SetDuration(TimeSpan duration)
    {
        _duration = duration;
        Sync(center: true);
    }

    /// <summary>The playing position: its cue, or the gap it falls in, is highlighted and brought to the middle.</summary>
    internal void ShowPosition(TimeSpan position)
    {
        _position = position;
        UpdateActive(center: !_seekingFromPanel);
    }

    internal bool Undo() => _document?.History.Undo() == true;

    internal bool Redo() => _document?.History.Redo() == true;

    // ---- Transcription and document ------------------------------------------------------------------------

    private void OnJobChanged(object? sender, EventArgs e)
    {
        var job = _job!;
        Progress.Visibility = job.State == TranscriptionState.Running ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = job.Progress;
        RetryButton.Visibility = job.State == TranscriptionState.Failed ? Visibility.Visible : Visibility.Collapsed;
        switch (job.State)
        {
            case TranscriptionState.Waiting:
                StatusText.Text = "Transcription en attente : elle commence après la transcription ou l'enregistrement en cours.";
                break;
            case TranscriptionState.Running:
                StatusText.Text = "Transcription du micro… " + job.Progress.ToString("P0", CultureInfo.CurrentCulture);
                break;
            case TranscriptionState.Failed:
                StatusText.Text = job.Error;
                break;
            case TranscriptionState.Canceled:
                StatusText.Text = "Transcription annulée.";
                break;
            case TranscriptionState.Done when _document is null:
                SetDocument(new SubtitleDocument(job.Cues));
                break;
        }
        SubtitlesChanged?.Invoke();
    }

    private void SetDocument(SubtitleDocument document)
    {
        _document = document;
        document.Changed += OnDocumentChanged;
        Sync(center: true);
        UpdateStatus();
        SubtitlesChanged?.Invoke();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        Sync(center: false);
        UpdateStatus();
        _saveTimer.Stop();
        _saveTimer.Start();
        SubtitlesChanged?.Invoke();
    }

    private void UpdateStatus()
    {
        if (_document is null)
            return;
        var count = _document.Cues.Count;
        StatusText.Text = (count switch
        {
            0 => "Aucune parole détectée. Ajoutez des sous-titres avec « Ajouter » dans la liste.",
            1 => "1 sous-titre",
            _ => $"{count} sous-titres"
        }) + (_saveError is null ? "" : "\n" + _saveError);
        StatusText.Foreground = _saveError is null ? DimBrush : ErrorBrush;
    }

    private void Save()
    {
        _saveTimer.Stop();
        if (_document is null || _file is null)
            return;
        try
        {
            _file.Save(_videoPath, _document.Cues);
            _saveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _saveError = "Les sous-titres n'ont pas pu être enregistrés : " + ex.Message;
        }
        UpdateStatus();
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_job is not null)
            _transcriptions?.Retry(_job);
    }

    // ---- Rows ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the rows match the timeline. Cue rows are updated in place and only gap rows come and go, so that the
    /// field being edited keeps its row, and the keyboard.
    /// </summary>
    private void Sync(bool center)
    {
        if (_document is null)
            return;
        _timeline = _document.Timeline(_duration);
        var rows = Rows.Children;
        for (var i = 0; i < _timeline.Count; i++)
        {
            var existing = i < rows.Count ? rows[i] : null;
            if (_timeline[i] is CueEntry cue)
            {
                if (existing is GapRow)
                {
                    rows.RemoveAt(i--);
                    continue;
                }
                if (existing is CueRow cueRow)
                    cueRow.Show(cue);
                else
                    rows.Insert(i, NewCueRow(cue));
            }
            else if (_timeline[i] is GapEntry gap)
            {
                if (existing is GapRow gapRow)
                    gapRow.Show(gap);
                else
                    rows.Insert(i, NewGapRow(gap));
            }
        }
        while (rows.Count > _timeline.Count)
            rows.RemoveAt(rows.Count - 1);

        foreach (UIElement row in rows)
            SetActive(row, false);
        _active = -1;
        UpdateActive(center);
    }

    private CueRow NewCueRow(CueEntry entry)
    {
        var row = new CueRow();
        row.Show(entry);
        foreach (var box in new[] { row.StartBox, row.EndBox, row.TextBox })
            box.GotKeyboardFocus += (_, _) => OnFieldEntered(row.Entry.Start);

        row.StartBox.LostKeyboardFocus += (_, _) => CommitTime(row, row.StartBox, row.Entry.Start, t => _document!.SetStart(row.Index, t));
        row.EndBox.LostKeyboardFocus += (_, _) => CommitTime(row, row.EndBox, row.Entry.End, t => _document!.SetEnd(row.Index, t));
        row.TextBox.LostKeyboardFocus += (_, _) => CommitText(row);
        row.StartBox.PreviewKeyDown += (_, e) => OnFieldKey(row, e, allowNewLine: false);
        row.EndBox.PreviewKeyDown += (_, e) => OnFieldKey(row, e, allowNewLine: false);
        row.TextBox.PreviewKeyDown += (_, e) => OnFieldKey(row, e, allowNewLine: true);

        row.DeleteButton.Click += (_, _) => _document?.Remove(row.Index);
        row.MouseLeftButtonDown += (_, _) => SeekFromPanel(row.Entry.Start);
        return row;
    }

    private GapRow NewGapRow(GapEntry entry)
    {
        var row = new GapRow();
        row.Show(entry);
        row.AddButton.Click += (_, _) =>
        {
            if (_document?.AddInGap(row.Entry) is { } index and >= 0)
                FocusText(index);
        };
        row.MouseLeftButtonDown += (_, _) => SeekFromPanel(row.Entry.Start);
        return row;
    }

    private void FocusText(int cueIndex)
    {
        foreach (UIElement row in Rows.Children)
        {
            if (row is CueRow cue && cue.Index == cueIndex)
                Dispatcher.BeginInvoke(() => cue.TextBox.Focus(), DispatcherPriority.Loaded);
        }
    }

    // ---- Editing -------------------------------------------------------------------------------------------

    private void OnFieldEntered(TimeSpan start)
    {
        EditingStarted?.Invoke();
        SeekFromPanel(start);
    }

    private void SeekFromPanel(TimeSpan time)
    {
        _seekingFromPanel = true;
        try
        {
            SeekRequested?.Invoke(time);
        }
        finally
        {
            _seekingFromPanel = false;
        }
    }

    /// <summary>Enter validates and hands the keys back to the player; Shift+Enter starts a new line; Escape cancels.</summary>
    private void OnFieldKey(CueRow row, KeyEventArgs e, bool allowNewLine)
    {
        if (e.Key == Key.Escape)
        {
            row.Show(row.Entry, force: true);
            row.ShowError(null);
            LeaveField();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !(allowNewLine && Keyboard.Modifiers == ModifierKeys.Shift))
        {
            // Losing the focus commits the field.
            LeaveField();
            e.Handled = true;
        }
    }

    private void LeaveField()
    {
        Window.GetWindow(this)?.Focus();
    }

    private void CommitTime(CueRow row, TextBox box, TimeSpan current, Func<TimeSpan, string?> apply)
    {
        if (_document is null || row.Index >= _document.Cues.Count)
            return;
        if (!Timecode.TryParse(box.Text, out var time))
        {
            row.ShowError("Temps illisible : écrivez-le par exemple 01:02.345.");
            row.Show(row.Entry, force: true);
            return;
        }
        if (time == current)
        {
            row.Show(row.Entry, force: true);
            return;
        }
        var error = apply(time);
        row.ShowError(error);
        if (error is not null)
            row.Show(row.Entry, force: true);
    }

    private void CommitText(CueRow row)
    {
        if (_document is null || row.Index >= _document.Cues.Count)
            return;
        if (!_document.SetText(row.Index, row.TextBox.Text))
            row.Show(row.Entry, force: true);
    }

    // ---- Following the video -------------------------------------------------------------------------------

    private void UpdateActive(bool center)
    {
        var index = SubtitleDocument.IndexAt(_timeline, _position);
        if (index != _active && index < Rows.Children.Count)
        {
            if (_active >= 0 && _active < Rows.Children.Count)
                SetActive(Rows.Children[_active], false);
            _active = index;
            if (index >= 0)
            {
                SetActive(Rows.Children[index], true);
                if (center)
                    CenterActive();
            }
        }
        ShowCue();
    }

    /// <summary>Tells the player when the cue to show changes: another one, an edited text, or a gap.</summary>
    private void ShowCue()
    {
        var text = _active >= 0 && _active < _timeline.Count && _timeline[_active] is CueEntry { Cue.Text: { Length: > 0 } cueText }
            ? cueText
            : null;
        if (text == _shownCue)
            return;
        _shownCue = text;
        CueShown?.Invoke(text);
    }

    private static void SetActive(UIElement row, bool active)
    {
        if (row is not Border border)
            return;
        border.Background = active ? ActiveBackground : Brushes.Transparent;
        border.BorderBrush = active ? ActiveBorder : Brushes.Transparent;
    }

    /// <summary>Scrolls the highlighted row to the middle, once it has been laid out.</summary>
    private void CenterActive()
    {
        if (_active < 0 || _active >= Rows.Children.Count || !IsVisible)
            return;
        var row = (FrameworkElement)Rows.Children[_active];
        if (row.ActualHeight == 0 || Scroller.ViewportHeight == 0)
        {
            if (!_centerPending)
            {
                _centerPending = true;
                Dispatcher.BeginInvoke(() =>
                {
                    _centerPending = false;
                    if (row.ActualHeight > 0)
                        CenterActive();
                }, DispatcherPriority.Loaded);
            }
            return;
        }
        var top = row.TransformToAncestor(Rows).Transform(new Point()).Y + Rows.Margin.Top;
        Scroller.ScrollToVerticalOffset(Math.Max(0, top + row.ActualHeight / 2 - Scroller.ViewportHeight / 2));
    }

    internal static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    // ---- Row controls --------------------------------------------------------------------------------------

    /// <summary>One cue: its start and end, its text, a delete button, and why the last change was refused.</summary>
    internal sealed class CueRow : Border
    {
        public CueRow()
        {
            StartBox = TimeBox("Début");
            EndBox = TimeBox("Fin");
            TextBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(4, 3, 4, 3),
                Background = FieldBackground,
                Foreground = TextBrush,
                BorderBrush = FieldBorder,
                CaretBrush = TextBrush,
                ToolTip = "Texte du sous-titre (Maj+Entrée pour une nouvelle ligne)"
            };
            ErrorText = new TextBlock { Foreground = ErrorBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            DeleteButton = new Button
            {
                Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") },
                Style = (Style)Application.Current.FindResource("ActionButton"),
                Focusable = false,
                Padding = new Thickness(6, 3, 6, 3),
                ToolTip = "Supprimer ce sous-titre"
            };

            var times = new DockPanel();
            DockPanel.SetDock(DeleteButton, Dock.Right);
            times.Children.Add(DeleteButton);
            times.Children.Add(StartBox);
            times.Children.Add(new TextBlock { Text = "→", Foreground = DimBrush, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            times.Children.Add(EndBox);
            times.Children.Add(SpeedText);
            times.LastChildFill = false;

            var content = new StackPanel();
            content.Children.Add(times);
            content.Children.Add(TextBox);
            content.Children.Add(ErrorText);
            Child = content;
            Padding = new Thickness(8, 6, 10, 8);
            BorderThickness = new Thickness(3, 0, 0, 0);
            BorderBrush = Brushes.Transparent;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
        }

        public TextBox StartBox { get; }
        public TextBox EndBox { get; }
        public TextBox TextBox { get; }
        public TextBlock ErrorText { get; }

        /// <summary>The reading speed, coloured when the cue is hard to read, with the reasons in its tooltip.</summary>
        public TextBlock SpeedText { get; } = new()
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11
        };
        public Button DeleteButton { get; }

        public int Index { get; private set; } = -1;

        public CueEntry Entry { get; private set; } = new(-1, new SubtitleCue(TimeSpan.Zero, TimeSpan.Zero, ""));

        /// <summary>
        /// Shows the cue. A field whose text already says the same is left alone, so that the caret does not jump.
        /// </summary>
        /// <param name="force">Puts the cue's values back even in the fields that say the same differently.</param>
        public void Show(CueEntry entry, bool force = false)
        {
            if (entry.Index != Index)
                ShowError(null);
            Index = entry.Index;
            Entry = entry;
            SetTime(StartBox, entry.Start, force);
            SetTime(EndBox, entry.End, force);
            ShowReadability(entry.Cue);
            var text = entry.Cue.Text.ReplaceLineEndings(Environment.NewLine);
            if (force || TextBox.Text.ReplaceLineEndings(Environment.NewLine).Trim() != text)
                TextBox.Text = text;
        }

        private void ShowReadability(SubtitleCue cue)
        {
            var readability = CueReadability.Of(cue);
            SpeedText.Text = readability.Speed;
            SpeedText.Foreground = readability.Level switch
            {
                ReadabilityLevel.Problem => ErrorBrush,
                ReadabilityLevel.Notice => NoticeBrush,
                _ => DimBrush
            };
            SpeedText.ToolTip = readability.ToolTip;
        }

        public void ShowError(string? error)
        {
            ErrorText.Text = error ?? "";
            ErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private static void SetTime(TextBox box, TimeSpan time, bool force)
        {
            if (force || !Timecode.TryParse(box.Text, out var shown) || shown != time)
                box.Text = Timecode.Format(time);
        }

        private static TextBox TimeBox(string tip) => new()
        {
            Width = 92,
            Padding = new Thickness(4, 2, 4, 2),
            FontFamily = new FontFamily("Consolas"),
            Background = FieldBackground,
            Foreground = TextBrush,
            BorderBrush = FieldBorder,
            CaretBrush = TextBrush,
            ToolTip = tip + " (minutes:secondes.millièmes)"
        };
    }

    /// <summary>Time without a subtitle: its span, and a button to add one there.</summary>
    internal sealed class GapRow : Border
    {
        private readonly TextBlock _label = new()
        {
            Foreground = DimBrush,
            FontStyle = FontStyles.Italic,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        public GapRow()
        {
            AddButton = new Button
            {
                Content = "+ Ajouter",
                Style = (Style)Application.Current.FindResource("ActionButton"),
                Focusable = false,
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Ajouter un sous-titre au début de ce passage"
            };
            var panel = new DockPanel();
            DockPanel.SetDock(AddButton, Dock.Right);
            panel.Children.Add(AddButton);
            panel.Children.Add(_label);
            Child = panel;
            Padding = new Thickness(8, 4, 10, 4);
            BorderThickness = new Thickness(3, 0, 0, 0);
            BorderBrush = Brushes.Transparent;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
        }

        public Button AddButton { get; }

        public GapEntry Entry { get; private set; } = new(TimeSpan.Zero, TimeSpan.Zero);

        public void Show(GapEntry entry)
        {
            Entry = entry;
            // Times only, in italics: the row must fit the panel's narrowest width, and it has no field to look like a cue.
            _label.Text = $"{Timecode.Format(entry.Start)} → {Timecode.Format(entry.End)}";
            ToolTip = $"Aucun sous-titre de {Timecode.Format(entry.Start)} à {Timecode.Format(entry.End)}. Cliquer pour y aller.";
            AddButton.Visibility = entry.End - entry.Start >= SubtitleDocument.MinDuration ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
