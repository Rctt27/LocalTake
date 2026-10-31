using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShowTime.Core.Geometry;
using ShowTime.Core.Hotkeys;
using ShowTime.Core.Ports;
using ShowTime.Core.Recording;
using ShowTime.Core.Settings;
using ShowTime.Core.Transcription;
using ShowTime.Infrastructure.Diagnostics;

namespace ShowTime.Infrastructure.IntegrationTests.Diagnostics;

public sealed class DiagnosticsReportTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(@"C:\Captures")) { HotkeyRegion = "PrintScreen" };
    private readonly IFfmpegPlatform _platform = Substitute.For<IFfmpegPlatform>();
    private readonly IAudioDeviceLister _audio = Substitute.For<IAudioDeviceLister>();
    private readonly IScreenGrabber _grabber = Substitute.For<IScreenGrabber>();
    private readonly IGlobalHotkeys _hotkeys = Substitute.For<IGlobalHotkeys>();
    private string? _modelError;

    public DiagnosticsReportTests()
    {
        var ffmpeg = _temp.File("ffmpeg.exe");
        File.WriteAllText(ffmpeg, "");
        _settings.FfmpegPath = ffmpeg;
        _platform.SystemAudioDevice.Returns("virtual-audio-capturer");
        _platform.SystemAudioRequirement.Returns("an audio output");
        _platform.CanRecordSystemAudio(Arg.Any<IReadOnlyCollection<string>>()).Returns(true);
        _platform.InstallHint.Returns("Install ffmpeg.");
        _audio.ListAsync(ffmpeg).Returns(["Microphone (USB)", "virtual-audio-capturer"]);
        _grabber.Grab(Arg.Any<PixelRect>()).Returns(new PixelBuffer(64, 64, new byte[64 * 64 * 4]));
        _grabber.VisibleWindows().Returns([]);
        _hotkeys.Register(Arg.Any<Hotkey>(), Arg.Any<Action>()).Returns(true);
        Directory.CreateDirectory(_temp.File("models"));
        File.WriteAllText(_temp.File(Path.Combine("models", "ggml-small-q5_1.bin")), "");
    }

    public void Dispose() => _temp.Dispose();

    private async Task<(int Code, string Output)> Run(IDisplays? displays = null, bool transcribes = true)
    {
        var paths = Substitute.For<IAppPaths>();
        paths.SettingsFile.Returns(_temp.File("settings.json"));
        paths.LogsFolder.Returns(_temp.Path);
        var locator = new FfmpegLocator(new FileSystem(), _temp.Path, null, "ffmpeg.exe", []);
        var models = new TranscriptionModelLocator(new FileSystem(), [_temp.File("models")]);
        var report = new DiagnosticsReport(paths, _settings, locator, _platform, _audio, _grabber, _hotkeys, models,
            NullLogger<DiagnosticsReport>.Instance, _ => _modelError, displays, transcribes);
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
        output.ShouldContain("Everything ShowTime needs is available.");
    }

    [Fact]
    public async Task Screens_are_listed()
    {
        var displays = Substitute.For<IDisplays>();
        displays.Screens().Returns([new PixelRect(0, 0, 3024, 1964), new PixelRect(2880, 0, 1920, 1080)]);

        var (_, output) = await Run(displays);

        output.ShouldContain("screens: 0 = 3024×1964 at 0,0; 1 = 1920×1080 at 2880,0");
    }

    [Fact]
    public async Task No_screen_line_without_displays()
    {
        var (_, output) = await Run();

        output.ShouldNotContain("screens:");
    }

    [Fact]
    public async Task The_transcription_model_is_found_and_loaded()
    {
        var (_, output) = await Run();

        output.ShouldContain("[ok]   transcription: " + _temp.File(Path.Combine("models", "ggml-small-q5_1.bin")));
    }

    [Fact]
    public async Task A_missing_transcription_model_is_reported()
    {
        File.Delete(_temp.File(Path.Combine("models", "ggml-small-q5_1.bin")));

        var (code, output) = await Run();

        code.ShouldBe(2);
        output.ShouldContain("[FAIL] transcription: FileNotFoundException: no Whisper model");
    }

    [Fact]
    public async Task A_model_that_does_not_load_is_reported()
    {
        _modelError = "whisper.dll not found";

        var (code, output) = await Run();

        code.ShouldBe(2);
        output.ShouldContain("does not load: whisper.dll not found");
    }

    [Fact]
    public async Task No_transcription_check_where_the_app_does_not_transcribe()
    {
        File.Delete(_temp.File(Path.Combine("models", "ggml-small-q5_1.bin")));

        var (code, output) = await Run(transcribes: false);

        code.ShouldBe(0);
        output.ShouldNotContain("transcription");
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
    public async Task The_computer_sound_is_checked_by_the_platform()
    {
        // A virtual audio driver may be listed but is not used: only the platform knows whether the sound can be recorded.
        _platform.CanRecordSystemAudio(Arg.Any<IReadOnlyCollection<string>>()).Returns(false);

        var (code, output) = await Run();

        code.ShouldBe(2);
        output.ShouldContain("[FAIL] computer sound");
        output.ShouldContain("missing: an audio output");
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
