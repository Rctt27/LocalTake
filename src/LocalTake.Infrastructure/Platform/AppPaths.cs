using System.IO.Abstractions;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;

namespace LocalTake.Infrastructure.Platform;

/// <summary>Where LocalTake keeps its files, following each platform's conventions.</summary>
public sealed record AppPaths(string SettingsFile, string LogsFolder, string DefaultOutputFolder) : IAppPaths
{
    private const string AppName = "LocalTake";

    /// <summary>Environment variables OneDrive sets to its sync roots.</summary>
    private static readonly string[] OneDriveVariables = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    public static AppPaths ForCurrentPlatform()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            var library = Path.Combine(home, "Library");
            return new AppPaths(
                Path.Combine(library, "Application Support", AppName, "settings.json"),
                Path.Combine(library, "Logs", AppName),
                // iCloud Drive syncs Desktop and Documents when asked to, never Pictures.
                Path.Combine(home, "Pictures", AppName));
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var outputFolder = OperatingSystem.IsWindows()
            ? WindowsOutputFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                home,
                [.. OneDriveVariables
                    .Select(Environment.GetEnvironmentVariable)
                    .OfType<string>()
                    .Where(root => root.Length > 0)])
            : Path.Combine(home, "Pictures", AppName);
        return new AppPaths(Path.Combine(appData, AppName, "settings.json"), Path.Combine(localAppData, AppName, "logs"), outputFolder);
    }

    /// <summary>
    /// Pictures\LocalTake, unless Pictures is synced by OneDrive: captures must stay on this computer.
    /// </summary>
    public static string WindowsOutputFolder(string pictures, string userProfile, IReadOnlyList<string> syncRoots)
    {
        var synced = string.IsNullOrEmpty(pictures) ||
                     syncRoots.Any(root => pictures.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        return synced ? Path.Combine(userProfile, "Captures " + AppName) : Path.Combine(pictures, AppName);
    }

    /// <summary>ffmpeg next to LocalTake, then on the PATH, then where package managers put it.</summary>
    public static FfmpegLocator CreateFfmpegLocator(IFileSystem fileSystem)
    {
        string[] wellKnown = OperatingSystem.IsWindows() ? [] : ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"];
        return new FfmpegLocator(
            fileSystem, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", wellKnown);
    }
}
