using System.IO.Abstractions;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Transcription;

namespace LocalTake.Infrastructure.Platform;

/// <summary>Where LocalTake keeps its files, following each platform's conventions.</summary>
/// <param name="ModelsFolders">Where to look for the transcription model: the one shipped with LocalTake comes first.</param>
public sealed record AppPaths(
    string SettingsFile, string LogsFolder, string DefaultOutputFolder, string WorkFolder, IReadOnlyList<string> ModelsFolders) : IAppPaths
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
            var support = Path.Combine(library, "Application Support", AppName);
            return new AppPaths(
                Path.Combine(support, "settings.json"),
                Path.Combine(library, "Logs", AppName),
                // iCloud Drive syncs Desktop and Documents when asked to, never Pictures.
                Path.Combine(home, "Pictures", AppName),
                Path.Combine(library, "Caches", AppName),
                // In the app bundle, Contents/Resources/models, next to the folder holding the assemblies.
                [ShippedModels, Path.Combine(AppContext.BaseDirectory, "..", "Resources", "models"), Path.Combine(support, "models")]);
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
        return new AppPaths(Path.Combine(appData, AppName, "settings.json"), Path.Combine(localAppData, AppName, "logs"), outputFolder,
            Path.Combine(localAppData, AppName, "work"), [ShippedModels, Path.Combine(localAppData, AppName, "models")]);
    }

    /// <summary>The release puts the model in a models folder next to LocalTake.</summary>
    private static string ShippedModels => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>
    /// Pictures\LocalTake, unless Pictures is synced by OneDrive: captures must stay on this computer.
    /// </summary>
    public static string WindowsOutputFolder(string pictures, string userProfile, IReadOnlyList<string> syncRoots)
    {
        var synced = string.IsNullOrEmpty(pictures) ||
                     syncRoots.Any(root => pictures.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        return synced ? Path.Combine(userProfile, "Captures " + AppName) : Path.Combine(pictures, AppName);
    }

    /// <summary>
    /// The ffmpeg shipped with LocalTake (next to the exe, or in the app bundle's Contents/MacOS), then the PATH, then
    /// where package managers put it, for a development build.
    /// </summary>
    public static FfmpegLocator CreateFfmpegLocator(IFileSystem fileSystem)
    {
        string[] wellKnown = OperatingSystem.IsWindows() ? [] : ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"];
        return new FfmpegLocator(
            fileSystem, FfmpegDirectory(AppContext.BaseDirectory), Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", wellKnown);
    }

    /// <summary>
    /// Where the shipped ffmpeg is: in a macOS app bundle the assemblies are in Contents/MonoBundle and the executables in
    /// Contents/MacOS; anywhere else, next to the assemblies.
    /// </summary>
    public static string FfmpegDirectory(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        const string Assemblies = ".app/Contents/MonoBundle";
        var trimmed = baseDirectory.TrimEnd('/');
        return trimmed.EndsWith(Assemblies, StringComparison.Ordinal)
            ? trimmed[..^"MonoBundle".Length] + "MacOS"
            : baseDirectory;
    }

    public TranscriptionModelLocator CreateModelLocator(IFileSystem fileSystem) => new(fileSystem, ModelsFolders);
}
