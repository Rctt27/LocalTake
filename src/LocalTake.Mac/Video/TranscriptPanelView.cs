using AppKit;
using CoreGraphics;
using LocalTake.Core.Transcription;
using ObjCRuntime;

namespace LocalTake.Mac.Video;

/// <summary>
/// The subtitles beside the video, cue by cue with the gaps between them, text and times editable, as on Windows. The
/// row at the playing position is highlighted and kept in the middle. The rules (transcription, edits, saving to the
/// .srt) are <see cref="SubtitleEditor"/>'s; the panel draws its rows.
/// </summary>
internal sealed class TranscriptPanelView : NSView
{
    internal static readonly NSColor ActiveBackground = NSColor.FromSrgb(0x2D / 255f, 0x34 / 255f, 0x40 / 255f, 1f);
    internal static readonly NSColor ActiveBar = NSColor.FromSrgb(0x0A / 255f, 0x64 / 255f, 0xAD / 255f, 1f);

    private readonly SubtitleEditor _editor;
    private readonly NSTextField _status = NSTextField.CreateLabel("");
    private readonly NSProgressIndicator _progress = new()
    {
        Indeterminate = false,
        MinValue = 0,
        MaxValue = 1,
        Style = NSProgressIndicatorStyle.Bar,
        Hidden = true
    };
    private readonly NSButton _retry;
    private readonly NSStackView _rows = new() { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Spacing = 0, Alignment = NSLayoutAttribute.Leading };
    private readonly NSScrollView _scroll;
    private int _active = -1;

    /// <summary>Set while a click in the panel moves the video: that row is under the mouse, it is not scrolled away.</summary>
    private bool _seekingFromPanel;

    public TranscriptPanelView(SubtitleEditor editor)
    {
        _editor = editor;
        WantsLayer = true;
        Layer!.BackgroundColor = NSColor.FromSrgb(0x25 / 255f, 0x25 / 255f, 0x26 / 255f, 1f).CGColor;

        var title = NSTextField.CreateLabel("Sous-titres");
        SetFont(title, NSFont.BoldSystemFontOfSize(14));
        _retry = NSButton.CreateButton("Réessayer", () => _editor.Retry());
        _retry.ToolTip = "Relancer la transcription du micro";
        _retry.Hidden = true;
        _retry.RefusesFirstResponder = true;
        var heading = NSStackView.FromViews([title, new NSView(), _retry]);
        heading.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;

        _status.TextColor = NSColor.SecondaryLabel;
        _status.LineBreakMode = NSLineBreakMode.ByWordWrapping;
        _status.MaximumNumberOfLines = 0;
        _status.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Horizontal);

        var hint = NSTextField.CreateLabel("Clic : aller au passage · Entrée : valider · Maj+Entrée : nouvelle ligne · Échap : annuler · ⌘Z : défaire");
        hint.TextColor = NSColor.TertiaryLabel;
        SetFont(hint, NSFont.SystemFontOfSize(11));
        hint.LineBreakMode = NSLineBreakMode.ByWordWrapping;
        hint.MaximumNumberOfLines = 0;
        hint.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Horizontal);

        // Not a table: a few hundred cues at most, and each row keeps its fields, so the one being edited keeps the keyboard.
        var document = new FlippedView();
        _rows.TranslatesAutoresizingMaskIntoConstraints = false;
        document.AddSubview(_rows);
        _scroll = new NSScrollView { HasVerticalScroller = true, DrawsBackground = false, DocumentView = document };
        document.TranslatesAutoresizingMaskIntoConstraints = false;

        foreach (var view in new NSView[] { heading, _status, _progress, _scroll, hint })
        {
            view.TranslatesAutoresizingMaskIntoConstraints = false;
            AddSubview(view);
        }
        NSLayoutConstraint.ActivateConstraints(
        [
            heading.TopAnchor.ConstraintEqualTo(TopAnchor, 8),
            heading.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 10),
            heading.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -10),
            _status.TopAnchor.ConstraintEqualTo(heading.BottomAnchor, 4),
            _status.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 10),
            _status.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -10),
            _progress.TopAnchor.ConstraintEqualTo(_status.BottomAnchor, 6),
            _progress.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 10),
            _progress.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -10),
            _scroll.TopAnchor.ConstraintEqualTo(_progress.BottomAnchor, 6),
            _scroll.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _scroll.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            hint.TopAnchor.ConstraintEqualTo(_scroll.BottomAnchor, 6),
            hint.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 10),
            hint.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -10),
            hint.BottomAnchor.ConstraintEqualTo(BottomAnchor, -6),
            document.TopAnchor.ConstraintEqualTo(_scroll.ContentView.TopAnchor),
            document.LeadingAnchor.ConstraintEqualTo(_scroll.ContentView.LeadingAnchor),
            document.WidthAnchor.ConstraintEqualTo(_scroll.ContentView.WidthAnchor),
            _rows.TopAnchor.ConstraintEqualTo(document.TopAnchor, 4),
            _rows.LeadingAnchor.ConstraintEqualTo(document.LeadingAnchor),
            _rows.TrailingAnchor.ConstraintEqualTo(document.TrailingAnchor),
            _rows.BottomAnchor.ConstraintEqualTo(document.BottomAnchor, -4)
        ]);

        editor.TimelineChanged += Sync;
        editor.ActiveChanged += OnActiveChanged;
        editor.StatusChanged += UpdateStatus;
        Sync(center: true);
        OnActiveChanged(center: true);
        UpdateStatus();
    }

    /// <summary>A row clicked or a field entered: the video goes to that place.</summary>
    public event Action<TimeSpan>? SeekRequested;

    /// <summary>A field is being edited: the video pauses, so that the rows stay still under the cursor.</summary>
    public event Action? EditingStarted;

    /// <summary>Enter or Escape left a field: the player takes the keyboard back.</summary>
    public event Action? EditingEnded;

    /// <summary>Whether a field of the panel has the keyboard: keys then type text instead of driving the player.</summary>
    public bool IsEditing =>
        Window?.FirstResponder is NSTextView { FieldEditor: true } editor && editor.WeakDelegate is NSView field && field.IsDescendantOf(this);

    private void UpdateStatus()
    {
        var status = _editor.Status;
        _status.StringValue = status.Text;
        _status.TextColor = status.IsError ? NSColor.SystemRed : NSColor.SecondaryLabel;
        _progress.Hidden = !status.ShowsProgress;
        _progress.DoubleValue = status.Progress;
        _retry.Hidden = !status.CanRetry;
    }

    // ---- Rows ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the rows match the timeline. Cue rows are updated in place and only gap rows come and go, so that the
    /// field being edited keeps its row, and the keyboard. The editor then says which row is active.
    /// </summary>
    private void Sync(bool center)
    {
        var timeline = _editor.Timeline;
        for (var i = 0; i < timeline.Count; i++)
        {
            var rows = _rows.ArrangedSubviews;
            var existing = i < rows.Length ? rows[i] : null;
            if (timeline[i] is CueEntry cue)
            {
                if (existing is GapRow)
                {
                    RemoveRow(existing);
                    i--;
                    continue;
                }
                if (existing is CueRow cueRow)
                    cueRow.Show(cue);
                else
                    InsertRow(NewCueRow(cue), i);
            }
            else if (timeline[i] is GapEntry gap)
            {
                if (existing is GapRow gapRow)
                    gapRow.Show(gap);
                else
                    InsertRow(NewGapRow(gap), i);
            }
        }
        while (_rows.ArrangedSubviews.Length > timeline.Count)
            RemoveRow(_rows.ArrangedSubviews[^1]);

        foreach (var row in _rows.ArrangedSubviews.OfType<Row>())
            row.SetActive(false);
        _active = -1;
    }

    private void InsertRow(Row row, int index)
    {
        _rows.InsertArrangedSubview(row, index);
        row.WidthAnchor.ConstraintEqualTo(_rows.WidthAnchor).Active = true;
    }

    private void RemoveRow(NSView row)
    {
        _rows.RemoveArrangedSubview(row);
        row.RemoveFromSuperview();
    }

    private CueRow NewCueRow(CueEntry entry)
    {
        var row = new CueRow();
        row.Show(entry);
        foreach (var field in new[] { row.StartField, row.EndField, row.TextField })
        {
            field.Entered += () => OnFieldEntered(row.Entry.Start);
            field.DoCommandBySelector = (_, textView, selector) => OnFieldCommand(row, field, textView, selector);
        }
        row.StartField.EditingEnded += (_, _) => CommitTime(row, row.StartField, row.Entry.Start, _editor.CommitStart);
        row.EndField.EditingEnded += (_, _) => CommitTime(row, row.EndField, row.Entry.End, _editor.CommitEnd);
        row.TextField.EditingEnded += (_, _) =>
        {
            if (!_editor.CommitText(row.Index, row.TextField.StringValue))
                row.Show(row.Entry, force: true);
        };
        row.DeleteRequested += () => _editor.Remove(row.Index);
        row.Clicked += () => SeekFromPanel(row.Entry.Start);
        return row;
    }

    private GapRow NewGapRow(GapEntry entry)
    {
        var row = new GapRow();
        row.Show(entry);
        row.AddRequested += () =>
        {
            if (_editor.AddInGap(row.Entry) is var index and >= 0)
                FocusText(index);
        };
        row.Clicked += () => SeekFromPanel(row.Entry.Start);
        return row;
    }

    private void FocusText(int cueIndex)
    {
        if (_rows.ArrangedSubviews.OfType<CueRow>().FirstOrDefault(r => r.Index == cueIndex) is { } row)
            Window?.MakeFirstResponder(row.TextField);
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
    private bool OnFieldCommand(CueRow row, CueField field, NSTextView textView, Selector selector)
    {
        switch (selector.Name)
        {
            case "insertNewline:" when field == row.TextField &&
                                       NSApplication.SharedApplication.CurrentEvent?.ModifierFlags.HasFlag(NSEventModifierMask.ShiftKeyMask) == true:
                NSApplication.SharedApplication.SendAction(new Selector("insertNewlineIgnoringFieldEditor:"), textView, textView);
                return true;
            case "cancelOperation:":
                row.Show(row.Entry, force: true);
                row.ShowError(null);
                LeaveField();
                return true;
            case "insertNewline:":
                // Ending the edit commits the field.
                LeaveField();
                return true;
            default:
                return false;
        }
    }

    private void LeaveField()
    {
        Window?.MakeFirstResponder(null);
        EditingEnded?.Invoke();
    }

    private void CommitTime(CueRow row, CueField field, TimeSpan current, Func<int, string, string?> commit)
    {
        if (row.Index >= _editor.Cues.Count)
            return;
        // The same time, written otherwise: shown as the cue has it, and an earlier refusal stays explained.
        if (Timecode.TryParse(field.StringValue, out var time) && time == current)
        {
            row.Show(row.Entry, force: true);
            return;
        }
        var error = commit(row.Index, field.StringValue);
        row.ShowError(error);
        if (error is not null)
            row.Show(row.Entry, force: true);
    }

    // ---- Following the video -------------------------------------------------------------------------------

    private void OnActiveChanged(bool center)
    {
        var rows = _rows.ArrangedSubviews;
        if (_active >= 0 && _active < rows.Length && rows[_active] is Row previous)
            previous.SetActive(false);
        _active = _editor.ActiveIndex;
        if (_active < 0 || _active >= rows.Length || rows[_active] is not Row row)
            return;
        row.SetActive(true);
        if (center && !_seekingFromPanel)
            CenterActive(row);
    }

    /// <summary>Scrolls the highlighted row to the middle.</summary>
    private void CenterActive(Row row)
    {
        _scroll.DocumentView?.LayoutSubtreeIfNeeded();
        var clip = _scroll.ContentView;
        var documentHeight = _scroll.DocumentView?.Frame.Height ?? 0;
        var frame = row.ConvertRectToView(row.Bounds, _scroll.DocumentView);
        var y = Math.Max(0, Math.Min(frame.Y + frame.Height / 2 - clip.Bounds.Height / 2, documentHeight - clip.Bounds.Height));
        clip.ScrollToPoint(new CGPoint(0, y));
        _scroll.ReflectScrolledClipView(clip);
    }

    private static void SetFont(NSTextField field, NSFont? font)
    {
        if (font is not null)
            field.Font = font;
    }

    // ---- Row views -----------------------------------------------------------------------------------------

    /// <summary>The scroll view's content, laid out from the top.</summary>
    private sealed class FlippedView : NSView
    {
        public override bool IsFlipped => true;
    }

    /// <summary>A text field that says when it is clicked or tabbed into.</summary>
    internal sealed class CueField : NSTextField
    {
        public event Action? Entered;

        public override bool BecomeFirstResponder()
        {
            var accepted = base.BecomeFirstResponder();
            if (accepted)
                Entered?.Invoke();
            return accepted;
        }
    }

    /// <summary>A row: a bar on its left and a background when it is at the playing position; a click goes there.</summary>
    internal class Row : NSView
    {
        private readonly NSView _bar = new() { WantsLayer = true, Hidden = true };

        protected Row()
        {
            WantsLayer = true;
            _bar.Layer!.BackgroundColor = ActiveBar.CGColor;
            _bar.TranslatesAutoresizingMaskIntoConstraints = false;
            AddSubview(_bar);
            NSLayoutConstraint.ActivateConstraints(
            [
                _bar.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
                _bar.TopAnchor.ConstraintEqualTo(TopAnchor),
                _bar.BottomAnchor.ConstraintEqualTo(BottomAnchor),
                _bar.WidthAnchor.ConstraintEqualTo(3)
            ]);
        }

        public event Action? Clicked;

        public void SetActive(bool active)
        {
            Layer!.BackgroundColor = active ? ActiveBackground.CGColor : NSColor.Clear.CGColor;
            _bar.Hidden = !active;
        }

        /// <summary>Lays the content out inside the row's padding.</summary>
        protected void Place(NSView content, nfloat top, nfloat bottom)
        {
            content.TranslatesAutoresizingMaskIntoConstraints = false;
            AddSubview(content);
            NSLayoutConstraint.ActivateConstraints(
            [
                content.TopAnchor.ConstraintEqualTo(TopAnchor, top),
                content.BottomAnchor.ConstraintEqualTo(BottomAnchor, -bottom),
                content.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 8),
                content.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -10)
            ]);
        }

        public override void MouseDown(NSEvent theEvent) => Clicked?.Invoke();

        public override void ResetCursorRects() => AddCursorRect(Bounds, NSCursor.PointingHandCursor);
    }

    /// <summary>One cue: its start and end, its reading speed, a delete button, its text, and why the last change was refused.</summary>
    internal sealed class CueRow : Row
    {
        private readonly NSTextField _speed = NSTextField.CreateLabel("");
        private readonly NSTextField _error = NSTextField.CreateLabel("");

        public CueRow()
        {
            StartField = TimeField("Début");
            EndField = TimeField("Fin");
            TextField = new CueField
            {
                UsesSingleLineMode = false,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
                ToolTip = "Texte du sous-titre (Maj+Entrée pour une nouvelle ligne)"
            };
            TextField.Cell.Wraps = true;
            TextField.Cell.Scrollable = false;
            SetFont(_speed, NSFont.SystemFontOfSize(11));
            _error.TextColor = NSColor.SystemRed;
            _error.LineBreakMode = NSLineBreakMode.ByWordWrapping;
            _error.MaximumNumberOfLines = 0;
            _error.Hidden = true;
            _error.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Horizontal);

            var delete = NSImage.GetSystemSymbol("trash", null) is { } image
                ? NSButton.CreateButton(image, () => DeleteRequested?.Invoke())
                : NSButton.CreateButton("Supprimer", () => DeleteRequested?.Invoke());
            delete.Bordered = false;
            delete.RefusesFirstResponder = true;
            delete.ToolTip = "Supprimer ce sous-titre";

            var arrow = NSTextField.CreateLabel("→");
            arrow.TextColor = NSColor.SecondaryLabel;
            var times = NSStackView.FromViews([StartField, arrow, EndField, _speed, new NSView(), delete]);
            times.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            times.Spacing = 6;

            var content = NSStackView.FromViews([times, TextField, _error]);
            content.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            content.Alignment = NSLayoutAttribute.Leading;
            content.Spacing = 6;
            Place(content, 6, 8);
            NSLayoutConstraint.ActivateConstraints(
            [
                times.WidthAnchor.ConstraintEqualTo(content.WidthAnchor),
                TextField.WidthAnchor.ConstraintEqualTo(content.WidthAnchor),
                // Two lines of text: a subtitle has at most two.
                TextField.HeightAnchor.ConstraintGreaterThanOrEqualTo(40),
                _error.WidthAnchor.ConstraintEqualTo(content.WidthAnchor)
            ]);
        }

        public event Action? DeleteRequested;

        public CueField StartField { get; }
        public CueField EndField { get; }
        public CueField TextField { get; }

        public int Index { get; private set; } = -1;

        public CueEntry Entry { get; private set; } = new(-1, new SubtitleCue(TimeSpan.Zero, TimeSpan.Zero, ""));

        /// <summary>Shows the cue. A field whose text already says the same is left alone, so that the caret does not jump.</summary>
        /// <param name="force">Puts the cue's values back even in the fields that say the same differently.</param>
        public void Show(CueEntry entry, bool force = false)
        {
            if (entry.Index != Index)
                ShowError(null);
            Index = entry.Index;
            Entry = entry;
            SetTime(StartField, entry.Start, force);
            SetTime(EndField, entry.End, force);
            var readability = CueReadability.Of(entry.Cue);
            _speed.StringValue = readability.Speed;
            _speed.TextColor = readability.Level switch
            {
                ReadabilityLevel.Problem => NSColor.SystemRed,
                ReadabilityLevel.Notice => NSColor.SystemYellow,
                _ => NSColor.SecondaryLabel
            };
            _speed.ToolTip = readability.ToolTip;
            if (force || TextField.StringValue.ReplaceLineEndings("\n").Trim() != entry.Cue.Text)
                TextField.StringValue = entry.Cue.Text;
        }

        public void ShowError(string? error)
        {
            _error.StringValue = error ?? "";
            _error.Hidden = error is null;
        }

        private static void SetTime(NSTextField field, TimeSpan time, bool force)
        {
            if (force || !Timecode.TryParse(field.StringValue, out var shown) || shown != time)
                field.StringValue = Timecode.Format(time);
        }

        private static CueField TimeField(string tip)
        {
            var field = new CueField { ToolTip = tip + " (minutes:secondes.millièmes)" };
            SetFont(field, NSFont.UserFixedPitchFontOfSize(12));
            field.WidthAnchor.ConstraintEqualTo(92).Active = true;
            return field;
        }
    }

    /// <summary>Time without a subtitle: its span, and a button to add one there.</summary>
    internal sealed class GapRow : Row
    {
        private readonly NSTextField _label = NSTextField.CreateLabel("");
        private readonly NSButton _add;

        public GapRow()
        {
            _label.TextColor = NSColor.SecondaryLabel;
            if (NSFont.SystemFontOfSize(NSFont.SystemFontSize) is { } font)
                SetFont(_label, NSFontManager.SharedFontManager.ConvertFont(font, NSFontTraitMask.Italic));
            _label.LineBreakMode = NSLineBreakMode.TruncatingTail;
            _label.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Horizontal);
            _add = NSButton.CreateButton("+ Ajouter", () => AddRequested?.Invoke());
            _add.RefusesFirstResponder = true;
            _add.ToolTip = "Ajouter un sous-titre au début de ce passage";
            var content = NSStackView.FromViews([_label, new NSView(), _add]);
            content.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
            Place(content, 4, 4);
        }

        public event Action? AddRequested;

        public GapEntry Entry { get; private set; } = new(TimeSpan.Zero, TimeSpan.Zero);

        public void Show(GapEntry entry)
        {
            Entry = entry;
            // Times only, in italics: the row must fit the panel's narrowest width, and it has no field to look like a cue.
            _label.StringValue = $"{Timecode.Format(entry.Start)} → {Timecode.Format(entry.End)}";
            ToolTip = $"Aucun sous-titre de {Timecode.Format(entry.Start)} à {Timecode.Format(entry.End)}. Cliquer pour y aller.";
            _add.Hidden = entry.End - entry.Start < SubtitleDocument.MinDuration;
        }
    }
}
