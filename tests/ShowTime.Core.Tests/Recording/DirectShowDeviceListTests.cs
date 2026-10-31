using ShowTime.Core.Recording;

namespace ShowTime.Core.Tests.Recording;

public sealed class DirectShowDeviceListTests
{
    private readonly DirectShowGdigrab _platform = new(() => true);

    [Fact]
    public void Recent_ffmpeg_builds_tag_each_device_with_its_kind()
    {
        const string output = """
            [dshow @ 000001c2] "Integrated Camera" (video)
            [dshow @ 000001c2]   Alternative name "@device_pnp_\\?\usb#vid_04f2&pid_b6d9"
            [dshow @ 000001c2] "Microphone (Realtek(R) Audio)" (audio)
            [dshow @ 000001c2]   Alternative name "@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{1}"
            [dshow @ 000001c2] "virtual-audio-capturer" (audio)
            [dshow @ 000001c2]   Alternative name "@device_sw_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\{8E14549B}"
            [dshow @ 000001c2] "OBS Virtual Camera" (none)
            dummy: Immediate exit requested
            """;
        _platform.ParseDeviceList(output).ShouldBe(["Microphone (Realtek(R) Audio)", "virtual-audio-capturer"]);
    }

    [Fact]
    public void Older_builds_list_audio_devices_under_a_heading()
    {
        const string output = """
            [dshow @ 0000] DirectShow video devices (some may be both video and audio devices)
            [dshow @ 0000]  "Integrated Camera"
            [dshow @ 0000]     Alternative name "@device_pnp_camera"
            [dshow @ 0000] DirectShow audio devices
            [dshow @ 0000]  "Microphone (USB)"
            [dshow @ 0000]     Alternative name "@device_cm_mic"
            [dshow @ 0000]  "Microphone (USB)"
            """;
        _platform.ParseDeviceList(output).ShouldBe(["Microphone (USB)"]);
    }

    [Fact]
    public void No_output_lists_no_device() => _platform.ParseDeviceList("").ShouldBeEmpty();

    [Fact]
    public void Lists_devices_with_dshow() =>
        _platform.ListDevicesArguments.ShouldBe(["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"]);

    [Fact]
    public void System_audio_is_the_virtual_audio_driver() => _platform.SystemAudioDevice.ShouldBe("virtual-audio-capturer");
}
