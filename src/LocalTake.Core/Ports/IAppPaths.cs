namespace LocalTake.Core.Ports;

/// <summary>Where LocalTake keeps its files on this computer.</summary>
public interface IAppPaths
{
    string SettingsFile { get; }

    /// <summary>The local log; it never leaves the computer.</summary>
    string LogsFolder { get; }

    /// <summary>Captures must stay on this computer: never a folder synced to the cloud.</summary>
    string DefaultOutputFolder { get; }

    /// <summary>Files LocalTake makes for itself, such as the microphone tracks to transcribe; local, never synced.</summary>
    string WorkFolder { get; }
}
