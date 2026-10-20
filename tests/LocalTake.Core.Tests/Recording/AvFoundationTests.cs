using LocalTake.Core.Geometry;
using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class AvFoundationTests
{
    private const string DeviceList = """
        [AVFoundation indev @ 0x7f8b1c004a00] AVFoundation video devices:
        [AVFoundation indev @ 0x7f8b1c004a00] [0] FaceTime HD Camera
        [AVFoundation indev @ 0x7f8b1c004a00] [1] Capture screen 0
        [AVFoundation indev @ 0x7f8b1c004a00] [2] Capture screen 1
        [AVFoundation indev @ 0x7f8b1c004a00] AVFoundation audio devices:
        [AVFoundation indev @ 0x7f8b1c004a00] [0] MacBook Pro Microphone
        [AVFoundation indev @ 0x7f8b1c004a00] [1] BlackHole 2ch
        [AVFoundation indev @ 0x7f8b1c004a00] [2] Microphone (USB) [Studio]
        : Input/output error
        """;

    /// <summary>Two displays side by side, the second at 1.5× on the right.</summary>
    private static readonly IReadOnlyList<PixelRect> Displays = [new(0, 0, 2880, 1800), new(2880, 0, 2560, 1440)];

    private readonly AvFoundation _platform = new(() => Displays);

    [Fact]
    public void Audio_devices_are_those_after_the_audio_heading() =>
        _platform.ParseDeviceList(DeviceList).ShouldBe(["MacBook Pro Microphone", "BlackHole 2ch", "Microphone (USB) [Studio]"]);

    [Fact]
    public void Lists_devices_with_avfoundation() =>
        _platform.ListDevicesArguments.ShouldBe(["-hide_banner", "-f", "avfoundation", "-list_devices", "true", "-i", ""]);

    [Fact]
    public void System_audio_is_BlackHole() => _platform.SystemAudioDevice.ShouldBe("BlackHole 2ch");

    [Fact]
    public void The_display_holding_the_area_is_recorded_and_cropped_to_it()
    {
        var inputs = _platform.BuildInputs(new PixelRect(3000, 100, 640, 480), new VideoOptions(30, null, false), null);

        inputs.AudioInputs.ShouldBeEmpty();
        inputs.VideoInput.ShouldBe(["-f", "avfoundation", "-thread_queue_size", "1024", "-capture_cursor", "1", "-framerate", "30", "-i", "Capture screen 1:none"]);
        inputs.VideoFilter.ShouldBe("crop=640:480:120:100");
    }

    [Fact]
    public void Each_audio_input_is_opened_by_name()
    {
        var inputs = _platform.BuildInputs(new PixelRect(0, 0, 100, 100), new VideoOptions(24, "MacBook Pro Microphone", true), null);
        inputs.AudioInputs.ShouldBe(
        [
            ["-f", "avfoundation", "-thread_queue_size", "1024", "-i", ":MacBook Pro Microphone"],
            ["-f", "avfoundation", "-thread_queue_size", "1024", "-i", ":BlackHole 2ch"]
        ]);
    }

    [Fact]
    public void An_area_over_two_displays_records_the_one_holding_most_of_it()
    {
        var inputs = _platform.BuildInputs(new PixelRect(2700, 0, 400, 300), new VideoOptions(30, null, false), null);
        inputs.VideoInput[^1].ShouldBe("Capture screen 1:none");
        // One display only: the part of the area on it, with even dimensions.
        inputs.VideoFilter.ShouldBe("crop=220:300:0:0");
    }

    [Fact]
    public void The_install_hint_names_Homebrew() => _platform.InstallHint.ShouldContain("brew install ffmpeg");

    [Fact]
    public void The_computer_sound_needs_BlackHole()
    {
        _platform.CanRecordSystemAudio(["MacBook Pro Microphone"]).ShouldBeFalse();
        _platform.CanRecordSystemAudio(["MacBook Pro Microphone", "BlackHole 2ch"]).ShouldBeTrue();
    }

    [Fact]
    public void BlackHole_is_tried_anyway_when_ffmpeg_could_not_list_the_devices() =>
        _platform.CanRecordSystemAudio([]).ShouldBeTrue();
}
