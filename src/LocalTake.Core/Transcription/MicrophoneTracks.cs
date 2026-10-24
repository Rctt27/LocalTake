using System.IO.Abstractions;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;

namespace LocalTake.Core.Transcription;

/// <summary>
/// The microphone tracks recorded for the transcription: WAV files in LocalTake's work folder, deleted once transcribed
/// or when the video's player closes without them. Recorded whenever there is a model, so that the player can still
/// transcribe a video recorded without the automatic transcription.
/// </summary>
public sealed class MicrophoneTracks(AppSettings settings, TranscriptionModelLocator models, IAppPaths paths, IFileSystem fileSystem)
{
    /// <summary>Whether a model is there to transcribe with: without one, the bar's toggle is disabled.</summary>
    public bool CanTranscribe => models.Find(settings.TranscriptionModelPath) is not null;

    /// <summary>A new track's path when it can be transcribed; null without a model. Only called with a microphone.</summary>
    public string? Allocate() => CanTranscribe ? NewPath() : null;

    /// <summary>A new WAV in the work folder, which is created if needed.</summary>
    public string NewPath()
    {
        fileSystem.Directory.CreateDirectory(paths.WorkFolder);
        return fileSystem.Path.Combine(paths.WorkFolder, Guid.NewGuid().ToString("N") + ".wav");
    }

    /// <summary>At startup: the tracks of a previous run, closed before they were transcribed.</summary>
    public void DeleteLeftovers()
    {
        if (!fileSystem.Directory.Exists(paths.WorkFolder))
            return;
        foreach (var track in fileSystem.Directory.GetFiles(paths.WorkFolder, "*.wav"))
        {
            try
            {
                fileSystem.File.Delete(track);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still in use by another LocalTake: the next start deletes it.
            }
        }
    }
}
