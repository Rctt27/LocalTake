namespace LocalTake.Core.Recording;

public interface IRecordingStatus
{
    /// <summary>From the area selection to the end of finalization; image captures wait meanwhile.</summary>
    bool IsBusy { get; }

    /// <summary>During the countdown and the recording, when ffmpeg needs the processor.</summary>
    bool IsRecording { get; }

    event EventHandler? StateChanged;
}

/// <param name="microphoneTrackPath">The microphone alone, to transcribe; null without a microphone or a model.</param>
/// <param name="transcribe">Whether the user asked, when the recording started, for its transcription straight away.</param>
public sealed class VideoSavedEventArgs(string path, bool fullScreen, string? microphoneTrackPath = null, bool transcribe = false)
    : EventArgs
{
    public string Path { get; } = path;

    /// <summary>Whether the whole screen was recorded rather than an area: "Refaire" records the same way.</summary>
    public bool FullScreen { get; } = fullScreen;

    public string? MicrophoneTrackPath { get; } = microphoneTrackPath;

    public bool Transcribe { get; } = transcribe;
}
