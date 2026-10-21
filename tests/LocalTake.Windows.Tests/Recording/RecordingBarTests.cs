using System.Windows;
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
        var bar = new RecordingBar(settings, Substitute.For<ISettingsStore>(), platform, Substitute.For<INativeWindows>(), canTranscribe: true);
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

    private static Task<(bool? Checked, bool Enabled, object Tip)> TranscriptionToggle(bool canTranscribe, bool microphone) => Sta.Run(() =>
    {
        var settings = new AppSettings(SettingsDefaults.ForWindows(@"C:\Captures")) { TranscribeMicrophone = true, RecordMicrophone = microphone };
        var bar = new RecordingBar(settings, Substitute.For<ISettingsStore>(), Substitute.For<IFfmpegPlatform>(),
            Substitute.For<INativeWindows>(), canTranscribe);
        var toggle = (bar.TranscriptionToggle.IsChecked, bar.TranscriptionToggle.IsEnabled, bar.TranscriptionToggle.ToolTip);
        bar.Close();
        return toggle;
    });

    [Fact]
    public async Task The_subtitles_toggle_shows_the_saved_choice_when_the_microphone_is_recorded()
    {
        var (isChecked, enabled, _) = await TranscriptionToggle(canTranscribe: true, microphone: true);
        isChecked.ShouldBe(true);
        enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_the_microphone_there_are_no_subtitles()
    {
        var (isChecked, enabled, tip) = await TranscriptionToggle(canTranscribe: true, microphone: false);
        isChecked.ShouldBe(false);
        enabled.ShouldBeFalse();
        tip.ShouldBe("Les sous-titres transcrivent le micro : activez-le d'abord.");
    }

    [Fact]
    public async Task Without_a_model_there_are_no_subtitles()
    {
        var (isChecked, enabled, tip) = await TranscriptionToggle(canTranscribe: false, microphone: true);
        isChecked.ShouldBe(false);
        enabled.ShouldBeFalse();
        tip.ShouldBe("Modèle de transcription introuvable : voir les réglages.");
    }

    private static Task<T> WithBar<T>(Func<RecordingBar, T> act) => Sta.Run(() =>
    {
        var bar = new RecordingBar(new AppSettings(SettingsDefaults.ForWindows(@"C:\\Captures")), Substitute.For<ISettingsStore>(),
            Substitute.For<IFfmpegPlatform>(), Substitute.For<INativeWindows>(), canTranscribe: true);
        try
        {
            bar.ShowReady("Ctrl+R", "Ctrl+D");
            return act(bar);
        }
        finally
        {
            bar.Close();
        }
    });

    private static void Click(RecordingBar bar) =>
        bar.StatusButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    [Fact]
    public async Task When_ready_the_status_is_a_play_button_that_starts_the_recording()
    {
        var (enabled, play, stop, hint, starts, stops) = await WithBar(bar =>
        {
            int starts = 0, stops = 0;
            bar.StartRequested += () => starts++;
            bar.StopRequested += () => stops++;
            Click(bar);
            return (bar.StatusButton.IsEnabled, bar.PlayIcon.Visibility == Visibility.Visible,
                bar.StopSquare.Visibility == Visibility.Visible, bar.StatusHint.Text, starts, stops);
        });

        enabled.ShouldBeTrue();
        play.ShouldBeTrue();
        stop.ShouldBeFalse();
        hint.ShouldBe("Démarrer (Ctrl+R)");
        (starts, stops).ShouldBe((1, 0));
    }

    [Fact]
    public async Task Once_recording_the_same_button_shows_the_elapsed_time_and_stops()
    {
        // ffmpeg usually captures before the end of the countdown: the bar goes from the countdown straight to
        // recording, without "Démarrage…". It used to keep Start, disabled, and never showed Stop.
        var (enabled, play, stop, hint, starts, stops) = await WithBar(bar =>
        {
            int starts = 0, stops = 0;
            bar.StartRequested += () => starts++;
            bar.StopRequested += () => stops++;
            bar.ShowCountdown(1);
            bar.ShowRecording();
            Click(bar);
            return (bar.StatusButton.IsEnabled, bar.PlayIcon.Visibility == Visibility.Visible,
                bar.StopSquare.Visibility == Visibility.Visible, bar.StatusHint.Text, starts, stops);
        });

        enabled.ShouldBeTrue();
        play.ShouldBeFalse();
        stop.ShouldBeTrue();
        hint.ShouldBe("Arrêter (Ctrl+D)");
        (starts, stops).ShouldBe((0, 1));
    }

    [Fact]
    public async Task While_ffmpeg_starts_the_button_already_stops()
    {
        var stops = await WithBar(bar =>
        {
            var stops = 0;
            bar.StopRequested += () => stops++;
            bar.ShowCountdown(1);
            bar.ShowStarting();
            Click(bar);
            return stops;
        });

        stops.ShouldBe(1);
    }

    [Fact]
    public async Task During_the_countdown_and_while_finishing_the_button_does_nothing()
    {
        var (countdown, finishing) = await WithBar(bar =>
        {
            bar.ShowCountdown(3);
            var countdown = bar.StatusButton.IsEnabled;
            bar.ShowRecording();
            bar.ShowFinishing();
            return (countdown, bar.StatusButton.IsEnabled);
        });

        countdown.ShouldBeFalse();
        finishing.ShouldBeFalse();
    }

    [Fact]
    public async Task The_computer_sound_does_not_need_TinyTake_s_driver_in_the_list()
    {
        var (enabled, _) = await SystemAudioToggle(canRecord: true, listed: ["Mic A"]);
        enabled.ShouldBeTrue();
    }
}
