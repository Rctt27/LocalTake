using System.IO;
using System.Windows;
using LocalTake.Core.Ports;
using LocalTake.Core.Transcription;
using NSubstitute;

namespace LocalTake.Windows.Tests.Recording;

public sealed class VideoPreviewSubtitleTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LocalTakeTests", Guid.NewGuid().ToString("N"));

    public VideoPreviewSubtitleTests() => Directory.CreateDirectory(_folder);

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

    private static readonly double[] Positions = [2, 4, 6];

    [Fact]
    public async Task The_subtitle_being_played_is_shown_over_the_video_on_a_dark_band()
    {
        var transcriptions = new TestTranscriptions(_folder);
        var video = Path.Combine(_folder, "video.mp4");
        File.WriteAllText(video, "");
        transcriptions.Subtitles.Save(video, [new SubtitleCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Un"),
            new SubtitleCue(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), "Deux\nlignes")]);

        var shown = await Sta.Run(() =>
        {
            var window = new VideoPreviewWindow(video, false, Substitute.For<IShell>(), Substitute.For<IFileTrash>(),
                transcriptions.Subtitles, transcriptions.Queue, null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            window.Show();
            window.Transcript.SetDuration(TimeSpan.FromSeconds(10));
            var result = new List<(bool Visible, string Text)>();
            foreach (var seconds in Positions)
            {
                window.Transcript.ShowPosition(TimeSpan.FromSeconds(seconds));
                result.Add((window.SubtitleBand.Visibility == Visibility.Visible, window.SubtitleText.Text));
            }
            var background = window.SubtitleBand.Background.ToString(System.Globalization.CultureInfo.InvariantCulture);
            window.Close();
            return (result, background);
        });

        shown.result.ShouldBe([(true, "Un"), (false, ""), (true, "Deux\nlignes")]);
        // Black, partly see-through: the text stays readable on any picture.
        shown.background.ShouldBe("#99000000");
    }
}
