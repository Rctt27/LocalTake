using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using NSubstitute;

namespace LocalTake.Windows.Tests.Recording;

public sealed class RecordingBarTests
{
    private static Task<(bool Enabled, object Tip)> SystemAudioToggle(bool canRecord, IReadOnlyCollection<string>? listed) => Sta.Run(() =>
    {
        var platform = Substitute.For<IFfmpegPlatform>();
        platform.CanRecordSystemAudio(Arg.Any<IReadOnlyCollection<string>>()).Returns(canRecord);
        platform.SystemAudioRequirement.Returns("une sortie audio (haut-parleurs ou casque)");
        var settings = new AppSettings(SettingsDefaults.ForWindows(@"C:\Captures")) { RecordSystemAudio = true };
        var bar = new RecordingBar(settings, Substitute.For<ISettingsStore>(), platform, Substitute.For<INativeWindows>());
        if (listed is not null)
            bar.SetAudioDevices(listed);
        var toggle = (bar.SystemAudioToggle.IsEnabled, bar.SystemAudioToggle.ToolTip);
        bar.Close();
        return toggle;
    });

    [Fact]
    public async Task Without_an_audio_output_the_computer_sound_is_off_before_ffmpeg_lists_anything()
    {
        var (enabled, tip) = await SystemAudioToggle(canRecord: false, listed: null);
        enabled.ShouldBeFalse();
        tip.ShouldBe("Nécessite une sortie audio (haut-parleurs ou casque) : introuvable sur cet ordinateur.");
    }

    [Fact]
    public async Task The_computer_sound_does_not_need_TinyTake_s_driver_in_the_list()
    {
        var (enabled, _) = await SystemAudioToggle(canRecord: true, listed: ["Mic A"]);
        enabled.ShouldBeTrue();
    }
}
