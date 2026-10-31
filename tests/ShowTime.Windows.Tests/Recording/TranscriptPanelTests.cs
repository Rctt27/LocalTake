using System.IO;
using System.IO.Abstractions;
using System.Windows;
using System.Windows.Input;
using NSubstitute;
using ShowTime.Core.Ports;
using ShowTime.Core.Recording;
using ShowTime.Core.Settings;
using ShowTime.Core.Transcription;

namespace ShowTime.Windows.Tests.Recording;

public sealed class TranscriptPanelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShowTimeTests", Guid.NewGuid().ToString("N"));
    private readonly string _video;
    private readonly SubtitleFile _file;
    private readonly ManualTranscriber _transcriber;
    private readonly Transcriptions _transcriptions;

    public TranscriptPanelTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "models"));
        File.WriteAllText(Path.Combine(_folder, "models", "ggml-small-q5_1.bin"), "");
        _video = Path.Combine(_folder, "video.mp4");
        File.WriteAllText(_video, "");
        var transcriptions = new TestTranscriptions(_folder);
        _file = transcriptions.Subtitles;
        _transcriber = transcriptions.Transcriber;
        _transcriptions = transcriptions.Queue;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static SubtitleCue Cue(double start, double end, string text) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    /// <summary>A panel in an off-screen window, with "Un" from 1 to 3 s and "Deux" from 5 to 8 s of a 10 s video.</summary>
    private (Window Window, TranscriptPanel Panel) Open(TranscriptionJob? job = null)
    {
        if (job is null)
            _file.Save(_video, [Cue(1, 3, "Un"), Cue(5, 8, "Deux")]);
        var panel = new TranscriptPanel();
        var window = new Window
        {
            Content = panel,
            Width = 360,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        panel.Load(_video, _file, _transcriptions, job);
        panel.SetDuration(TimeSpan.FromSeconds(10));
        window.Show();
        Sta.DoEvents();
        return (window, panel);
    }

    private static TranscriptPanel.CueRow CueRow(TranscriptPanel panel, int index) =>
        panel.Rows.Children.OfType<TranscriptPanel.CueRow>().Single(r => r.Index == index);

    private static void LoseFocus(UIElement element) =>
        element.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, element, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

    [Fact]
    public async Task The_rows_are_the_cues_and_the_gaps_between_them()
    {
        var rows = await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var kinds = panel.Rows.Children.Cast<UIElement>().Select(r => r.GetType().Name).ToList();
            window.Close();
            return kinds;
        });

        rows.ShouldBe(["GapRow", "CueRow", "GapRow", "CueRow", "GapRow"]);
    }

    /// <summary>In "Un", between the cues, in "Deux", after the last cue.</summary>
    private static readonly double[] Positions = [2.0, 4.0, 6.0, 9.5];

    [Fact]
    public async Task The_row_at_the_playing_position_is_highlighted_cue_or_gap()
    {
        var active = await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var result = new List<int>();
            foreach (var seconds in Positions)
            {
                panel.ShowPosition(TimeSpan.FromSeconds(seconds));
                result.Add(panel.Rows.Children.Cast<System.Windows.Controls.Border>().ToList()
                    .FindIndex(r => r.Background == TranscriptPanel.ActiveBackground));
            }
            window.Close();
            return result;
        });

        active.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task A_click_on_a_row_moves_the_video_to_its_start()
    {
        var seeks = await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var times = new List<TimeSpan>();
            panel.SeekRequested += times.Add;
            var gap = panel.Rows.Children[2];
            gap.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            window.Close();
            return times;
        });

        seeks.ShouldBe([TimeSpan.FromSeconds(3)]);
    }

    [Fact]
    public async Task An_edited_text_is_saved_to_the_srt()
    {
        await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var row = CueRow(panel, 1);
            row.TextBox.Text = "  Deux,\r\ntrois  ";
            LoseFocus(row.TextBox);
            window.Close();
            panel.Close(discard: false);
        });

        _file.Load(_video)!.Cues.ShouldBe([Cue(1, 3, "Un"), Cue(5, 8, "Deux,\ntrois")]);
    }

    [Fact]
    public async Task A_refused_time_is_explained_and_put_back()
    {
        var (error, shown) = await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var row = CueRow(panel, 1);
            row.StartBox.Text = "00:02.500";
            LoseFocus(row.StartBox);
            var result = (row.ErrorText.Text, row.StartBox.Text);
            window.Close();
            return result;
        });

        error.ShouldBe("Le début chevauche le sous-titre précédent, qui finit à 00:03.000.");
        shown.ShouldBe("00:05.000");
    }

    [Fact]
    public async Task A_moved_end_opens_a_gap_and_the_rows_follow()
    {
        var rows = await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var row = CueRow(panel, 0);
            row.EndBox.Text = "2";
            LoseFocus(row.EndBox);
            var kinds = panel.Rows.Children.Cast<UIElement>().Select(r => r.GetType().Name).ToList();
            window.Close();
            return (kinds, panel.Cues[0].End);
        });

        rows.kinds.ShouldBe(["GapRow", "CueRow", "GapRow", "CueRow", "GapRow"]);
        rows.End.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_cue_too_fast_to_read_says_so_beside_its_times()
    {
        var (easy, fast) = await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var row = CueRow(panel, 0);
            var before = row.SpeedText.Text;
            // 40 characters in the 2 s of "Un": 20 per second.
            row.TextBox.Text = "On choisit la zone de l'écran à filmer.";
            LoseFocus(row.TextBox);
            var after = (row.SpeedText.Text, (string)row.SpeedText.ToolTip);
            window.Close();
            return (before, after);
        });

        easy.ShouldBe("1 car./s");
        fast.Text.ShouldBe("20 car./s");
        fast.Item2.ShouldBe("20 caractères par seconde : trop rapide à lire (17 au plus).");
    }

    [Fact]
    public async Task Undo_puts_back_the_previous_text()
    {
        var text = await Sta.Run(() =>
        {
            var (window, panel) = Open();
            var row = CueRow(panel, 0);
            row.TextBox.Text = "Changé";
            LoseFocus(row.TextBox);
            panel.Undo();
            var shown = CueRow(panel, 0).TextBox.Text;
            window.Close();
            return shown;
        });

        text.ShouldBe("Un");
    }

    [Fact]
    public async Task The_subtitles_appear_when_the_transcription_is_done()
    {
        var audio = Path.Combine(_folder, "mic.wav");
        File.WriteAllText(audio, "");

        var (whileRunning, rows) = await Sta.Run(() =>
        {
            var job = _transcriptions.Enqueue(_video, audio);
            var (window, panel) = Open(job);
            _transcriber.Progress!(0.5);
            var status = panel.StatusText.Text;
            _transcriber.Complete([new TranscribedWord(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2), "Bonjour")]);
            var count = panel.Rows.Children.Count;
            window.Close();
            return (status, count);
        });

        whileRunning.ShouldBe("Transcription du micro… " + 0.5.ToString("P0", System.Globalization.CultureInfo.CurrentCulture));
        rows.ShouldBe(3);
        // Cut by the tutorial profile: the cue lingers half a second after the word.
        _file.Load(_video)!.Cues.ShouldBe([Cue(0.5, 2.5, "Bonjour")]);
    }

    [Fact]
    public async Task A_discarded_video_stops_its_transcription()
    {
        var audio = Path.Combine(_folder, "mic.wav");
        File.WriteAllText(audio, "");

        var state = await Sta.Run(() =>
        {
            var job = _transcriptions.Enqueue(_video, audio);
            var (window, panel) = Open(job);
            panel.Close(discard: true);
            window.Close();
            return job.State;
        });

        state.ShouldBe(TranscriptionState.Canceled);
        File.Exists(audio).ShouldBeFalse();
    }
}
