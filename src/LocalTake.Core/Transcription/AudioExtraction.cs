namespace LocalTake.Core.Transcription;

/// <summary>The ffmpeg command lines that give Whisper its sound.</summary>
public static class AudioExtraction
{
    /// <summary>16 kHz mono 16-bit PCM: what Whisper reads, as ffmpeg output options.</summary>
    public static IReadOnlyList<string> WhisperFormat { get; } = ["-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le"];

    /// <summary>The first sound of a video, as a WAV for Whisper.</summary>
    public static IReadOnlyList<string> Arguments(string videoPath, string audioPath) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-i", videoPath, "-map", "0:a:0", "-vn", .. WhisperFormat, audioPath];

    /// <summary>Whether ffmpeg's error says that the video has no sound: then there is nothing to transcribe.</summary>
    public static bool SaysNoSound(string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Contains("matches no streams", StringComparison.Ordinal) ||
               error.Contains("does not contain any stream", StringComparison.Ordinal);
    }
}
