using System.Diagnostics;
using System.IO.Abstractions;
using NSubstitute;
using ShowTime.Core.Ports;
using ShowTime.Core.Recording;
using ShowTime.Core.Settings;
using ShowTime.Infrastructure.Platform;
using ShowTime.Infrastructure.Transcription;

namespace ShowTime.Infrastructure.IntegrationTests.Transcription;

/// <summary>Videos made by ffmpeg's own test sources; skipped when ffmpeg is not installed.</summary>
public sealed class FfmpegAudioExtractorTests : IDisposable
{
    private static readonly FfmpegLocator Locator = AppPaths.CreateFfmpegLocator(new FileSystem());

    private readonly TempFolder _folder = new();
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(@"C:\Captures"));

    public void Dispose() => _folder.Dispose();

    private FfmpegAudioExtractor Extractor => new(Locator, _settings, Substitute.For<IFfmpegPlatform>());

    private static string RequireFfmpeg()
    {
        var ffmpeg = Locator.Find("");
        Assert.SkipWhen(ffmpeg is null, "ffmpeg is not installed.");
        return ffmpeg!;
    }

    private static async Task<string> RunAsync(string ffmpeg, params string[] args)
    {
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    [Fact]
    public async Task The_sound_of_a_video_is_written_as_16_kHz_mono_wav()
    {
        var ffmpeg = RequireFfmpeg();
        var video = _folder.File("video.mp4");
        await RunAsync(ffmpeg, "-y", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=2", "-f", "lavfi", "-i",
            "sine=duration=2", "-shortest", video);
        var audio = _folder.File("sound.wav");

        await Extractor.ExtractAsync(video, audio, TestContext.Current.CancellationToken);

        var probe = await RunAsync(ffmpeg, "-hide_banner", "-i", audio);
        probe.ShouldContain("Audio: pcm_s16le");
        // ffmpeg 5 writes "mono", 6.1 "1 channels".
        probe.ShouldMatch(@"16000 Hz, (mono|1 channels)");
    }

    [Fact]
    public async Task A_video_without_sound_says_so()
    {
        var ffmpeg = RequireFfmpeg();
        var video = _folder.File("silent.mp4");
        await RunAsync(ffmpeg, "-y", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1", video);

        var error = await Should.ThrowAsync<InvalidOperationException>(
            Extractor.ExtractAsync(video, _folder.File("none.wav"), TestContext.Current.CancellationToken));

        error.Message.ShouldBe("La vidéo n'a pas de son à transcrire.");
    }

    [Fact]
    public async Task Without_ffmpeg_the_message_says_how_to_install_it()
    {
        _settings.FfmpegPath = _folder.File("missing-ffmpeg.exe");

        var error = await Should.ThrowAsync<InvalidOperationException>(
            Extractor.ExtractAsync(_folder.File("video.mp4"), _folder.File("sound.wav"), TestContext.Current.CancellationToken));

        error.Message.ShouldStartWith("ffmpeg est introuvable");
    }
}
