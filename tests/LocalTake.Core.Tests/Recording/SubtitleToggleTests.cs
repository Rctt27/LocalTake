using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

/// <summary>The "Sous-titres" toggle of the recording bar.</summary>
public sealed class SubtitleToggleTests
{
    [Fact]
    public void Without_a_model_it_is_off_and_says_so() =>
        SubtitleToggle.For(canTranscribe: false, microphoneOn: true, transcribe: true, locked: false)
            .ShouldBe(new ToggleState(false, false, "Modèle de transcription introuvable : voir les réglages."));

    [Fact]
    public void Without_the_microphone_it_is_off_and_says_so() =>
        SubtitleToggle.For(canTranscribe: true, microphoneOn: false, transcribe: true, locked: false)
            .ShouldBe(new ToggleState(false, false, "Les sous-titres transcrivent le micro : activez-le d'abord."));

    [Fact]
    public void On_it_says_what_happens_after_the_recording() =>
        SubtitleToggle.For(canTranscribe: true, microphoneOn: true, transcribe: true, locked: false)
            .ShouldBe(new ToggleState(true, true,
                "Ce que vous dites au micro sera transcrit en sous-titres après l'enregistrement. Cliquer pour ne pas le transcrire."));

    [Fact]
    public void Off_it_offers_to_transcribe() =>
        SubtitleToggle.For(canTranscribe: true, microphoneOn: true, transcribe: false, locked: false)
            .ShouldBe(new ToggleState(false, true, "Pas de sous-titres. Cliquer pour transcrire le micro après l'enregistrement."));

    /// <summary>Once the recording has started, the choice is made: the toggle keeps its state but cannot change.</summary>
    [Fact]
    public void Locked_it_keeps_its_state_but_cannot_change()
    {
        var state = SubtitleToggle.For(canTranscribe: true, microphoneOn: true, transcribe: true, locked: true);

        state.IsChecked.ShouldBeTrue();
        state.IsEnabled.ShouldBeFalse();
    }
}
