using System.IO.Abstractions;

namespace ShowTime.Core.Recording;

/// <summary>Finds ffmpeg on this computer. ShowTime never downloads it: that would be a network call.</summary>
/// <param name="wellKnownDirectories">
/// Searched after the PATH: an app started from the macOS Finder does not get the shell's PATH, where Homebrew is
/// added.
/// </param>
public sealed class FfmpegLocator(
    IFileSystem fileSystem, string appDirectory, string? pathVariable, string executableName, IReadOnlyList<string> wellKnownDirectories)
{
    /// <summary>The configured path when there is one, otherwise next to ShowTime, then the PATH.</summary>
    /// <returns>The executable, or null when it cannot be found.</returns>
    public string? Find(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return fileSystem.File.Exists(configuredPath) ? configuredPath : null;

        var pathDirectories = (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in pathDirectories.Prepend(appDirectory).Concat(wellKnownDirectories))
        {
            try
            {
                var candidate = fileSystem.Path.Combine(directory.Trim().Trim('"'), executableName);
                if (fileSystem.File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }
        return null;
    }
}
