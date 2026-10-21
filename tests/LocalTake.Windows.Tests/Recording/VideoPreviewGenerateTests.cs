using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using LocalTake.Core.Ports;
using LocalTake.Core.Transcription;
using NSubstitute;

namespace LocalTake.Windows.Tests.Recording;

/// <summary>"Générer les sous-titres" in the player's header, for a video recorded without the automatic transcription.</summary>
public sealed class VideoPreviewGenerateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LocalTakeTests", Guid.NewGuid().ToString("N"));
    private readonly string _video;
    private readonly string _track;

    public VideoPreviewGenerateTests()
    {
        Directory.CreateDirectory(_folder);
        _video = Path.Combine(_folder, "video.mp4");
        File.WriteAllText(_video, "");
        _track = Path.Combine(_folder, "mic.wav");
        File.WriteAllText(_track, "RIFF");
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

    private static VideoPreviewWindow Open(TestTranscriptions transcriptions, string video, string? track)
    {
        var window = new VideoPreviewWindow(video, false, Substitute.For<IShell>(), Substitute.For<IFileTrash>(),
            transcriptions.Subtitles, transcriptions.Queue, null, track)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        window.Show();
        return window;
    }

    private static bool Shown(UIElement element) => element.Visibility == Visibility.Visible;

    private static void Click(VideoPreviewWindow window) =>
        window.GenerateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [Fact]
    public async Task A_video_without_subtitles_offers_to_generate_them()
    {
        var transcriptions = new TestTranscriptions(_folder);

        var (button, panel) = await Sta.Run(() =>
        {
            var window = Open(transcriptions, _video, null);
            var shown = (Shown(window.GenerateButton), Shown(window.Transcript));
            window.Close();
            return shown;
        });

        button.ShouldBeTrue();
        panel.ShouldBeFalse();
    }

    [Fact]
    public async Task A_video_with_subtitles_shows_them_instead()
    {
        var transcriptions = new TestTranscriptions(_folder);
        transcriptions.Subtitles.Save(_video, [new SubtitleCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Bonjour")]);

        var (button, panel) = await Sta.Run(() =>
        {
            var window = Open(transcriptions, _video, null);
            var shown = (Shown(window.GenerateButton), Shown(window.Transcript));
            window.Close();
            return shown;
        });

        button.ShouldBeFalse();
        panel.ShouldBeTrue();
    }

    [Fact]
    public async Task An_empty_subtitle_file_counts_as_no_subtitles()
    {
        var transcriptions = new TestTranscriptions(_folder);
        File.WriteAllText(SubtitleFile.PathFor(_video), "");

        var (button, panel) = await Sta.Run(() =>
        {
            var window = Open(transcriptions, _video, null);
            var shown = (Shown(window.GenerateButton), Shown(window.Transcript));
            window.Close();
            return shown;
        });

        button.ShouldBeTrue();
        panel.ShouldBeFalse();
    }

    [Fact]
    public async Task Generating_transcribes_the_microphone_track_kept_with_the_video()
    {
        var transcriptions = new TestTranscriptions(_folder);

        var (panel, buttonWhileRunning, buttonWhenNothingSaid) = await Sta.Run(() =>
        {
            var window = Open(transcriptions, _video, _track);
            Click(window);
            var running = (Shown(window.Transcript), Shown(window.GenerateButton));
            transcriptions.Transcriber.Complete([]);
            var afterwards = Shown(window.GenerateButton);
            window.Close();
            return (running.Item1, running.Item2, afterwards);
        });

        transcriptions.Transcriber.AudioPath.ShouldBe(_track);
        transcriptions.Extractor.VideoPath.ShouldBeNull();
        panel.ShouldBeTrue();
        buttonWhileRunning.ShouldBeFalse();
        // Nothing heard: no subtitles, so it can be tried again.
        buttonWhenNothingSaid.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_track_the_sound_of_the_video_is_transcribed()
    {
        var transcriptions = new TestTranscriptions(_folder);

        await Sta.Run(() =>
        {
            var window = Open(transcriptions, _video, null);
            Click(window);
            window.Close();
        });

        transcriptions.Extractor.VideoPath.ShouldBe(_video);
    }

    [Fact]
    public async Task Without_a_model_the_button_is_disabled_and_says_why()
    {
        var transcriptions = new TestTranscriptions(_folder, withModel: false);

        var (enabled, tip) = await Sta.Run(() =>
        {
            var window = Open(transcriptions, _video, _track);
            var state = (window.GenerateButton.IsEnabled, window.GenerateButton.ToolTip as string);
            window.Close();
            return state;
        });

        enabled.ShouldBeFalse();
        tip.ShouldBe("Modèle de transcription introuvable : voir les réglages.");
    }

    [Fact]
    public async Task Closing_without_generating_deletes_the_kept_track()
    {
        var transcriptions = new TestTranscriptions(_folder);

        await Sta.Run(() => Open(transcriptions, _video, _track).Close());

        File.Exists(_track).ShouldBeFalse();
    }
}
