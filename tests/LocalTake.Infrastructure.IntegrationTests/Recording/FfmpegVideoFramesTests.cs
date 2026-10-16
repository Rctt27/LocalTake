using System.IO.Abstractions;
using LocalTake.Core.Recording;
using LocalTake.Infrastructure.Platform;
using LocalTake.Infrastructure.Recording;

namespace LocalTake.Infrastructure.IntegrationTests.Recording;

public sealed class FfmpegVideoFramesTests : IDisposable
{
    private static readonly string? Ffmpeg = AppPaths.CreateFfmpegLocator(new FileSystem()).Find("");

    private readonly TempFolder _folder = new();
    private readonly FfmpegVideoFrames _frames = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>Three seconds of ffmpeg's test pattern, 320×240, with a tone.</summary>
    private async Task<string> TestVideoAsync()
    {
        Assert.SkipWhen(Ffmpeg is null, "ffmpeg is not installed.");
        var path = _folder.File("clip.mp4");
        var inputs = new CaptureInputs(
            [["-f", "lavfi", "-i", "sine=frequency=440:duration=3"]],
            ["-f", "lavfi", "-i", "testsrc=size=320x240:rate=30:duration=3"]);
        using var recorder = FfmpegVideoRecorder.Start(Ffmpeg!, inputs, path, _ => { });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(path) || new FileInfo(path).Length == 0 || !await recorder.StopAsync())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        return path;
    }

    [Fact]
    public async Task Probe_reads_the_duration_and_the_size()
    {
        var path = await TestVideoAsync();
        var info = (await _frames.ProbeAsync(Ffmpeg!, path))!;
        info.Width.ShouldBe(320);
        info.Height.ShouldBe(240);
        info.HasAudio.ShouldBeTrue();
        info.Duration.TotalSeconds.ShouldBe(3, 0.2);
    }

    [Fact]
    public async Task Frame_is_decoded_at_the_requested_width()
    {
        var path = await TestVideoAsync();
        var info = (await _frames.ProbeAsync(Ffmpeg!, path))!;
        var frame = (await _frames.FrameAsync(Ffmpeg!, path, TimeSpan.FromSeconds(1.5), 160, info, TestContext.Current.CancellationToken))!;
        frame.Width.ShouldBe(160);
        frame.Height.ShouldBe(120);
        frame.Pixels.Length.ShouldBe(160 * 120 * 4);
        frame.Pixels.Where((_, i) => i % 4 == 3).ShouldAllBe(a => a == 255);
    }

    [Fact]
    public async Task A_file_that_is_not_a_video_probes_to_nothing()
    {
        Assert.SkipWhen(Ffmpeg is null, "ffmpeg is not installed.");
        var path = _folder.File("text.mp4");
        await File.WriteAllTextAsync(path, "not a video", TestContext.Current.CancellationToken);
        (await _frames.ProbeAsync(Ffmpeg!, path)).ShouldBeNull();
    }
}
