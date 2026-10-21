namespace LocalTake.Core.Ports;

/// <summary>The sound of a recorded video, for a transcription asked for after the recording, without its microphone track.</summary>
public interface IAudioExtractor
{
    /// <summary>Writes the video's first sound as a 16 kHz mono WAV, what Whisper reads.</summary>
    /// <exception cref="InvalidOperationException">With a message in French for the user, such as a video without sound.</exception>
    Task ExtractAsync(string videoPath, string audioPath, CancellationToken cancellationToken);
}
