// Native calls: these tests run on the macOS CI runner only.
#pragma warning disable CA1416

using System.Diagnostics;
using LocalTake.Core.Recording;
using LocalTake.Platform.MacOS;

namespace LocalTake.Platform.Tests.MacOS;

/// <summary>The FIFO through which ffmpeg reads the Mac's sound. The reader stands in for ffmpeg, or is ffmpeg.</summary>
public sealed class MacAudioPipeTests : IDisposable
{
    private static readonly RawAudioFormat Format = new("f32le", 48000, 2);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "localtake-pipe-" + Guid.NewGuid().ToString("N"));
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public MacAudioPipeTests()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private TimeSpan Now() => _clock.Elapsed;

    private MacAudioPipe NewPipe() => new(_folder, Format, Now);

    /// <summary>Opens the reading end, as ffmpeg does, and waits until the pipe has noticed.</summary>
    private static async Task<FileStream> OpenAsync(MacAudioPipe pipe)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipe.Opened += () => opened.TrySetResult();
        var reader = await Task.Run(() => new FileStream(pipe.Input.PipePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1));
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        return reader;
    }

    /// <summary>Reads what comes for <paramref name="duration"/>.</summary>
    private static async Task<byte[]> ReadForAsync(FileStream reader, TimeSpan duration)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[65536];
        var clock = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(duration);
        try
        {
            while (clock.Elapsed < duration)
            {
                var read = await reader.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                    break;
                bytes.Write(buffer, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
            // The time is up.
        }
        return bytes.ToArray();
    }

    [Fact]
    public async Task ffmpeg_opening_the_fifo_starts_the_timeline()
    {
        using var pipe = NewPipe();
        await using var reader = await OpenAsync(pipe);

        pipe.Write(Now(), Enumerable.Repeat((byte)7, 480 * Format.BlockAlign).ToArray());
        var bytes = await ReadForAsync(reader, TimeSpan.FromMilliseconds(500));

        bytes.Count(b => b == 7).ShouldBe(480 * Format.BlockAlign);
    }

    [Fact]
    public async Task An_idle_pipe_is_padded_with_silence()
    {
        using var pipe = NewPipe();
        await using var reader = await OpenAsync(pipe);

        var bytes = await ReadForAsync(reader, TimeSpan.FromSeconds(1.5));

        var expected = (1.5 - AudioTimeline.Slack.TotalSeconds) * Format.SampleRate * Format.BlockAlign;
        var tolerance = 0.06 * Format.SampleRate * Format.BlockAlign;
        ((double)bytes.Length).ShouldBeInRange(expected - tolerance, expected + tolerance);
        bytes.ShouldAllBe(b => b == 0);
    }

    [Fact]
    public void Disposing_before_ffmpeg_opens_returns_and_removes_the_fifo()
    {
        var pipe = NewPipe();
        File.Exists(pipe.Input.PipePath).ShouldBeTrue();

        var clock = Stopwatch.StartNew();
        pipe.Dispose();

        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        File.Exists(pipe.Input.PipePath).ShouldBeFalse();
    }

    [Fact]
    public async Task ffmpeg_reads_the_pipe()
    {
        var ffmpeg = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "ffmpeg")).FirstOrDefault(File.Exists);
        Assert.SkipUnless(ffmpeg is not null, "Needs ffmpeg on the PATH.");
        using var pipe = NewPipe();
        var output = Path.Combine(_folder, "out.wav");

        var start = new ProcessStartInfo(ffmpeg!) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in new[] { "-hide_banner", "-y", "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", pipe.Input.PipePath, "-t", "2", output })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        process.ExitCode.ShouldBe(0, await errors);
        // Two seconds of 16-bit stereo at 48 kHz, plus the WAV header: the padding kept the track at length.
        new FileInfo(output).Length.ShouldBeInRange(2 * 48000 * 4, 2 * 48000 * 4 + 200);
    }
}
