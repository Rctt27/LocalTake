namespace ShowTime.Core.Recording;

/// <summary>A toggle of the recording bar as it should look.</summary>
public sealed record ToggleState(bool IsChecked, bool IsEnabled, string ToolTip);

/// <summary>
/// The recording bar's "Sous-titres" toggle. Only the microphone is transcribed: without it, there is nothing to put
/// in the subtitles.
/// </summary>
public static class SubtitleToggle
{
    /// <param name="canTranscribe">Whether there is a model to transcribe the microphone with.</param>
    /// <param name="transcribe">The setting, <c>TranscribeMicrophone</c>.</param>
    /// <param name="locked">The recording has started: the choice is made.</param>
    public static ToggleState For(bool canTranscribe, bool microphoneOn, bool transcribe, bool locked) => new(
        canTranscribe && microphoneOn && transcribe,
        canTranscribe && microphoneOn && !locked,
        !canTranscribe ? "Modèle de transcription introuvable : voir les réglages."
        : !microphoneOn ? "Les sous-titres transcrivent le micro : activez-le d'abord."
        : transcribe
            ? "Ce que vous dites au micro sera transcrit en sous-titres après l'enregistrement. Cliquer pour ne pas le transcrire."
            : "Pas de sous-titres. Cliquer pour transcrire le micro après l'enregistrement.");
}
