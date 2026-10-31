using System.IO.Abstractions.TestingHelpers;
using System.Text;
using ShowTime.Core.Transcription;

namespace ShowTime.Core.Tests.Transcription;

public sealed class SubtitleFileTests
{
    private static readonly string Video = Path.Combine(Path.GetTempPath(), "ShowTimeSrt", "Capture 1.mp4");
    private static readonly string Srt = Path.Combine(Path.GetTempPath(), "ShowTimeSrt", "Capture 1.srt");

    private readonly MockFileSystem _fs = new();
    private SubtitleFile File => new(_fs);

    private static SubtitleCue Cue(double start, double end, string text) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    [Fact]
    public void The_subtitles_are_next_to_the_video_with_the_same_name() => SubtitleFile.PathFor(Video).ShouldBe(Srt);

    [Fact]
    public void Save_writes_utf8_with_a_bom_which_windows_players_need_to_read_accents()
    {
        _fs.AddDirectory(Path.GetDirectoryName(Video)!);

        File.Save(Video, [Cue(0, 1, "Déjà")]);

        var bytes = _fs.File.ReadAllBytes(Srt);
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).ShouldBe(SrtFormat.Write([Cue(0, 1, "Déjà")]));
    }

    [Fact]
    public void Save_leaves_out_cues_without_text_and_replaces_the_previous_file()
    {
        _fs.AddFile(Srt, new MockFileData("ancien"));

        File.Save(Video, [Cue(0, 1, "Un"), Cue(1, 2, ""), Cue(2, 3, "Trois")]);

        File.Load(Video)!.Cues.ShouldBe([Cue(0, 1, "Un"), Cue(2, 3, "Trois")]);
        _fs.Directory.GetFiles(Path.GetDirectoryName(Video)!).ShouldBe([Srt]);
    }

    [Fact]
    public void Without_any_cue_left_the_file_is_deleted()
    {
        _fs.AddFile(Srt, new MockFileData("ancien"));

        File.Save(Video, [Cue(0, 1, "")]);
        File.Save(Video, []);

        _fs.File.Exists(Srt).ShouldBeFalse();
    }

    [Fact]
    public void Load_without_a_file_is_null() => File.Load(Video).ShouldBeNull();
}
