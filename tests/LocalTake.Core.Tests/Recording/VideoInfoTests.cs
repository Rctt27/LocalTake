using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class VideoInfoTests
{
    private const string Probe = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:\Out\video.mp4':
          Metadata:
            major_brand     : isom
          Duration: 00:01:05.23, start: 0.000000, bitrate: 1234 kb/s
          Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1280x720 [SAR 1:1 DAR 16:9], 1200 kb/s, 30 fps, 30 tbr, 15360 tbn (default)
          Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo, fltp, 160 kb/s (default)
        At least one output file must be specified
        """;

    [Fact]
    public void Parse_reads_the_duration_and_the_video_size()
    {
        var info = VideoInfo.Parse(Probe)!;
        info.Duration.ShouldBe(TimeSpan.FromSeconds(65.23));
        info.Width.ShouldBe(1280);
        info.Height.ShouldBe(720);
        info.HasAudio.ShouldBeTrue();
    }

    [Fact]
    public void Parse_of_a_file_ffmpeg_cannot_read_is_null() =>
        VideoInfo.Parse("C:\\x.mp4: Invalid data found when processing input").ShouldBeNull();

    [Fact]
    public void An_unknown_duration_reads_as_zero() =>
        VideoInfo.Parse("  Duration: N/A, bitrate: N/A\n  Stream #0:0: Video: h264, yuv420p, 640x480, 30 fps")!.Duration.ShouldBe(TimeSpan.Zero);

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(65.7, "00:01:05")]
    [InlineData(3725, "01:02:05")]
    public void Format_writes_hours_minutes_and_seconds(double seconds, string expected) =>
        VideoInfo.Format(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
}
