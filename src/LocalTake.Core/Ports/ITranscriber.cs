using LocalTake.Core.Transcription;

namespace LocalTake.Core.Ports;

/// <summary>Speech to text, on this computer: nothing is sent anywhere.</summary>
public interface ITranscriber
{
    /// <summary>What is said in a 16 kHz mono WAV, word by word, timed from its start.</summary>
    /// <param name="modelPath">The Whisper model.</param>
    /// <param name="language">A language code such as "fr", or "auto" to detect it.</param>
    /// <param name="onProgress">From 0 to 1, called on a worker thread.</param>
    Task<IReadOnlyList<TranscribedWord>> TranscribeAsync(
        string audioPath, string modelPath, string language, Action<double> onProgress, CancellationToken cancellationToken);
}
