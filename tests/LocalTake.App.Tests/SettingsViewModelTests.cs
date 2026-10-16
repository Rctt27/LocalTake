using System.IO.Abstractions.TestingHelpers;
using LocalTake.App.Settings;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using NSubstitute;

namespace LocalTake.App.Tests;

public sealed class SettingsViewModelTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "LocalTakeVm", "Captures");

    private readonly MockFileSystem _fs = new();
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(Folder));
    private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
    private readonly IAudioDeviceLister _lister = Substitute.For<IAudioDeviceLister>();
    private readonly IStartupRegistration _startup = Substitute.For<IStartupRegistration>();

    private SettingsViewModel Create(HotkeyNaming naming = HotkeyNaming.Windows)
    {
        var ffmpeg = Path.Combine(Path.GetTempPath(), "LocalTakeVm", "ffmpeg.exe");
        _fs.AddFile(ffmpeg, new MockFileData(""));
        var locator = new FfmpegLocator(_fs, Path.GetDirectoryName(ffmpeg)!, null, "ffmpeg.exe", []);
        var context = new SettingsContext(
            _settings, new SettingsEditor(_settings, _store, _fs, @"C:\Users\Moi\Captures"), SettingsDefaults.ForWindows(Folder),
            locator, new DirectShowGdigrab(), _lister, _startup, naming);
        return new SettingsViewModel(context);
    }

    [Fact]
    public void Saving_applies_the_form_and_closes()
    {
        var vm = Create();
        var finished = false;
        vm.Finished += (_, _) => finished = true;
        vm.FileNamePattern = "Shot_%NUM%";
        vm.SelectedFormat = SettingsViewModel.Formats[1];
        vm.StartWithSystem = true;

        vm.SaveCommand.Execute(null);

        vm.Error.ShouldBeNull();
        finished.ShouldBeTrue();
        _settings.FileNamePattern.ShouldBe("Shot_%NUM%");
        _settings.Format.ShouldBe("jpg");
        _store.Received(1).Save(_settings);
        _startup.Received(1).SetEnabled(true);
    }

    [Fact]
    public void An_invalid_form_shows_the_error_and_stays_open()
    {
        var vm = Create();
        var finished = false;
        vm.Finished += (_, _) => finished = true;
        vm.HotkeyWindow = vm.HotkeyRegion;

        vm.SaveCommand.Execute(null);

        vm.Error.ShouldBe("Le raccourci PrintScreen est attribué deux fois.");
        finished.ShouldBeFalse();
    }

    [Fact]
    public void Default_shortcuts_are_restored()
    {
        var vm = Create();
        vm.HotkeyRegion = "Ctrl+Q";
        vm.DefaultHotkeysCommand.Execute(null);
        vm.HotkeyRegion.ShouldBe("PrintScreen");
        vm.HotkeyVideoStop.ShouldBe("Ctrl+D");
    }

    [Fact]
    public void The_preview_shows_a_file_name_of_the_pattern()
    {
        var vm = Create();
        vm.FileNamePattern = "Shot_%NUM%";
        vm.PatternPreview.ShouldStartWith("Exemple : Shot_000001.png");
    }

    [Fact]
    public async Task Audio_devices_fill_the_microphones_and_tell_whether_the_computer_sound_can_be_recorded()
    {
        _lister.ListAsync(Arg.Any<string>()).Returns(["Mic A", "virtual-audio-capturer"]);
        var vm = Create();

        await vm.LoadAudioDevicesAsync();

        vm.Microphones.Select(m => m.Label).ShouldBe(["Premier micro détecté", "Mic A"]);
        vm.SystemAudioAvailable.ShouldBeTrue();
        vm.FfmpegMissing.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_ffmpeg_is_explained()
    {
        var vm = Create();
        vm.FfmpegPath = @"C:\nowhere\ffmpeg.exe";
        vm.FfmpegMissing.ShouldBeTrue();
        vm.FfmpegStatus.ShouldContain("winget install Gyan.FFmpeg");
    }

    [Fact]
    public void Labels_follow_the_platform()
    {
        Create(HotkeyNaming.MacOS).StartupLabel.ShouldBe("Lancer LocalTake à l'ouverture de session");
        Create(HotkeyNaming.Windows).DefaultHotkeysLabel.ShouldBe("Raccourcis de TinyTake");
    }
}
