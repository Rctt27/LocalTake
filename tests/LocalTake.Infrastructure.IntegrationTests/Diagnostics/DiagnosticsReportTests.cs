using System.IO.Abstractions;
using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using LocalTake.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LocalTake.Infrastructure.IntegrationTests.Diagnostics;

public sealed class DiagnosticsReportTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(@"C:\Captures")) { HotkeyRegion = "PrintScreen" };
    private readonly IFfmpegPlatform _platform = Substitute.For<IFfmpegPlatform>();
    private readonly IAudioDeviceLister _audio = Substitute.For<IAudioDeviceLister>();
    private readonly IScreenGrabber _grabber = Substitute.For<IScreenGrabber>();
    private readonly IGlobalHotkeys _hotkeys = Substitute.For<IGlobalHotkeys>();

    public DiagnosticsReportTests()
    {
        var ffmpeg = _temp.File("ffmpeg.exe");
        File.WriteAllText(ffmpeg, "");
        _settings.FfmpegPath = ffmpeg;
        _platform.SystemAudioDevice.Returns("virtual-audio-capturer");
        _platform.InstallHint.Returns("Install ffmpeg.");
        _audio.ListAsync(ffmpeg).Returns(["Microphone (USB)", "virtual-audio-capturer"]);
        _grabber.Grab(Arg.Any<PixelRect>()).Returns(new PixelBuffer(64, 64, new byte[64 * 64 * 4]));
        _grabber.VisibleWindows().Returns([]);
        _hotkeys.Register(Arg.Any<Hotkey>(), Arg.Any<Action>()).Returns(true);
    }

    public void Dispose() => _temp.Dispose();

    private async Task<(int Code, string Output)> Run()
    {
        var paths = Substitute.For<IAppPaths>();
        paths.SettingsFile.Returns(_temp.File("settings.json"));
        paths.LogsFolder.Returns(_temp.Path);
        var locator = new FfmpegLocator(new FileSystem(), _temp.Path, null, "ffmpeg.exe", []);
        var report = new DiagnosticsReport(paths, _settings, locator, _platform, _audio, _grabber, _hotkeys, NullLogger<DiagnosticsReport>.Instance);
        using var output = new StringWriter();
        var code = await report.RunAsync(output);
        return (code, output.ToString());
    }

    [Fact]
    public async Task All_checks_pass_returns_zero()
    {
        var (code, output) = await Run();

        code.ShouldBe(0);
        output.ShouldContain("[ok]   ffmpeg");
        output.ShouldContain("[ok]   global shortcut: PrintScreen accepted");
        output.ShouldContain("Everything LocalTake needs is available.");
    }

    [Fact]
    public async Task Refused_shortcut_is_reported()
    {
        _hotkeys.Register(Arg.Any<Hotkey>(), Arg.Any<Action>()).Returns(false);

        var (code, output) = await Run();

        code.ShouldBe(2);
        output.ShouldContain("[FAIL] global shortcut");
        output.ShouldContain("1 check(s) failed.");
    }

    [Fact]
    public async Task Missing_ffmpeg_skips_audio_checks()
    {
        _settings.FfmpegPath = _temp.File("missing.exe");

        var (_, output) = await Run();

        output.ShouldContain("[FAIL] ffmpeg");
        output.ShouldContain("Install ffmpeg.");
        output.ShouldNotContain("audio inputs");
    }

    [Fact]
    public async Task Shortcuts_are_released_after_the_check()
    {
        await Run();

        _hotkeys.Received(1).UnregisterAll();
    }

    /// <summary>
    /// Shortcuts belong to the thread that created their window: after waiting for ffmpeg, the report must come back
    /// to the caller's thread (the UI thread) before registering one.
    /// </summary>
    [Fact]
    public async Task Shortcut_is_checked_on_the_callers_context()
    {
        var context = new RecordingContext();
        SynchronizationContext? atRegister = null;
        _audio.ListAsync(Arg.Any<string>()).Returns(async _ =>
        {
            await Task.Delay(10);
            return (IReadOnlyList<string>)["virtual-audio-capturer"];
        });
        _hotkeys.Register(Arg.Any<Hotkey>(), Arg.Any<Action>()).Returns(_ =>
        {
            atRegister = SynchronizationContext.Current;
            return true;
        });

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            await Run();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        atRegister.ShouldBeSameAs(context);
    }

    /// <summary>Runs continuations at once, with itself as the current context, as a UI thread's context would.</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                d(state);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }
}
