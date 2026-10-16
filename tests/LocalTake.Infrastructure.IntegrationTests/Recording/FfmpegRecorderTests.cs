using System.Diagnostics;
using System.IO.Abstractions;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Infrastructure.Platform;
using LocalTake.Infrastructure.Recording;

namespace LocalTake.Infrastructure.IntegrationTests.Recording;

/// <summary>
/// Records with ffmpeg's own test sources (a pattern and a tone) instead of the screen, with the production encoding.
/// Skipped when ffmpeg is not installed.
/// </summary>
public sealed class FfmpegRecorderTests : IDisposable
{
    private static readonly string? Ffmpeg = AppPaths.CreateFfmpegLocator(new FileSystem()).Find("");

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static string RequireFfmpeg()
    {
        Assert.SkipWhen(Ffmpeg is null, "ffmpeg is not installed.");
        return Ffmpeg!;
    }

    private static CaptureInputs TestSources(bool audio) => new(
        audio ? [["-re", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"]] : [],
        ["-re", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30"]);

    [Fact]
    public async Task Stop_writes_a_playable_MP4_with_its_video_and_audio()
    {
        var ffmpeg = RequireFfmpeg();
        var output = _folder.File("video.mp4");
        var unexpected = false;

        using var recorder = FfmpegVideoRecorder.Start(ffmpeg, TestSources(audio: true), output, _ => unexpected = true);
        (await recorder.Capturing.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).ShouldBeTrue();
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        (await recorder.StopAsync()).ShouldBeTrue();

        unexpected.ShouldBeFalse();
        var probe = await RunFfmpegAsync(ffmpeg, "-hide_banner", "-i", output, "-f", "null", "-");
        probe.ExitCode.ShouldBe(0);
        probe.Output.ShouldContain("Video: h264");
        probe.Output.ShouldContain("Audio: aac");
    }

    [Fact]
    public async Task Stop_keeps_the_end_of_the_sound()
    {
        // "q" used to discard about 0.35 s of audio held behind the video frames (see CLAUDE.md).
        var ffmpeg = RequireFfmpeg();
        var output = _folder.File("ends.mp4");
        using var recorder = FfmpegVideoRecorder.Start(ffmpeg, TestSources(audio: true), output, _ => { });
        await recorder.Capturing.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        (await recorder.StopAsync()).ShouldBeTrue();

        var video = await StreamDurationAsync(ffmpeg, output, "v:0");
        var audio = await StreamDurationAsync(ffmpeg, output, "a:0");
        Math.Abs(video - audio).ShouldBeLessThan(0.25);
    }

    [Fact]
    public async Task Kill_stops_at_once()
    {
        var ffmpeg = RequireFfmpeg();
        using var recorder = FfmpegVideoRecorder.Start(ffmpeg, TestSources(audio: false), _folder.File("killed.mp4"), _ => { });
        await recorder.Capturing.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var watch = Stopwatch.StartNew();
        recorder.Kill();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task An_ffmpeg_that_fails_on_its_own_is_reported_with_its_errors()
    {
        var ffmpeg = RequireFfmpeg();
        var exited = new TaskCompletionSource<IVideoRecorder>();
        var broken = new CaptureInputs([], ["-f", "lavfi", "-i", "nosuchsource"]);

        using var recorder = FfmpegVideoRecorder.Start(ffmpeg, broken, _folder.File("broken.mp4"), r => exited.TrySetResult(r));

        (await exited.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).ShouldBeSameAs(recorder);
        (await recorder.Capturing).ShouldBeFalse();
        recorder.ErrorTail.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_factory_records_the_platform_inputs()
    {
        var ffmpeg = RequireFfmpeg();
        var platform = new TestSourcePlatform();
        var factory = new FfmpegVideoRecorderFactory(platform);
        using var recorder = factory.Start(ffmpeg, new PixelRect(0, 0, 320, 240), _folder.File("factory.mp4"), new VideoOptions(30, null, false), _ => { });
        (await recorder.Capturing.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await recorder.StopAsync()).ShouldBeTrue();
        platform.Area.ShouldBe(new PixelRect(0, 0, 320, 240));
    }

    [Fact]
    public async Task The_audio_device_lister_returns_an_empty_list_when_ffmpeg_cannot_run()
    {
        var lister = new FfmpegAudioDeviceLister(new DirectShowGdigrab());
        (await lister.ListAsync(_folder.File("not-ffmpeg.exe"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_audio_device_lister_runs_ffmpeg_and_parses_its_output()
    {
        var ffmpeg = RequireFfmpeg();
        var platform = new TestSourcePlatform();
        await new FfmpegAudioDeviceLister(platform).ListAsync(ffmpeg);
        platform.ListedOutput.ShouldContain("ffmpeg version");
    }

    private static async Task<(int ExitCode, string Output)> RunFfmpegAsync(string ffmpeg, params string[] args)
    {
        var start = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stderr + await stdout);
    }

    /// <summary>The time of the last packet of a stream, in seconds, read from ffmpeg's framecrc output.</summary>
    private static async Task<double> StreamDurationAsync(string ffmpeg, string file, string stream)
    {
        var (_, output) = await RunFfmpegAsync(ffmpeg, "-v", "error", "-i", file, "-map", "0:" + stream, "-c", "copy", "-f", "framecrc", "-");
        var last = output.Split('\n').Last(l => l.Length > 0 && !l.StartsWith('#'));
        // stream_index, dts, pts, duration, size, hash — with a "#tb" header giving the time base.
        var timeBase = output.Split('\n').First(l => l.StartsWith("#tb", StringComparison.Ordinal)).Split(':')[1].Trim().Split('/');
        var fields = last.Split(',').Select(f => f.Trim()).ToArray();
        var pts = long.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture);
        var duration = long.Parse(fields[3], System.Globalization.CultureInfo.InvariantCulture);
        return (pts + duration) * double.Parse(timeBase[0], System.Globalization.CultureInfo.InvariantCulture)
               / double.Parse(timeBase[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A platform whose "screen" is ffmpeg's test pattern.</summary>
    private sealed class TestSourcePlatform : IFfmpegPlatform
    {
        public PixelRect? Area { get; private set; }
        public string ListedOutput { get; private set; } = "";
        public string SystemAudioDevice => "none";
        public string InstallHint => "";
        public IReadOnlyList<string> ListDevicesArguments { get; } = ["-version"];

        public IReadOnlyList<string> ParseDeviceList(string output)
        {
            ListedOutput = output;
            return [];
        }

        public CaptureInputs BuildInputs(PixelRect area, VideoOptions options)
        {
            Area = area;
            return TestSources(audio: false);
        }
    }
}
