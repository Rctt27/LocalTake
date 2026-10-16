namespace LocalTake.Core.Recording;

public interface IRecordingStatus
{
    /// <summary>From the area selection to the end of finalization; image captures wait meanwhile.</summary>
    bool IsBusy { get; }
}

public sealed class VideoSavedEventArgs(string path, bool fullScreen) : EventArgs
{
    public string Path { get; } = path;

    /// <summary>Whether the whole screen was recorded rather than an area: "Refaire" records the same way.</summary>
    public bool FullScreen { get; } = fullScreen;
}
