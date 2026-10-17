using System.IO.Abstractions;
using LocalTake.App.Adapters;
using LocalTake.App.Capture;
using LocalTake.App.Editor;
using LocalTake.App.Recording;
using LocalTake.App.Settings;
using LocalTake.App.Tray;
using LocalTake.App.Video;
using LocalTake.Core.Capture;
using LocalTake.Core.Files;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using LocalTake.Infrastructure.Diagnostics;
using LocalTake.Infrastructure.Logging;
using LocalTake.Infrastructure.Platform;
using LocalTake.Infrastructure.Recording;
using LocalTake.Infrastructure.Settings;
using LocalTake.Platform.MacOS;
using LocalTake.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalTake.App;

/// <summary>The composition root: the core's use cases, the portable infrastructure and this platform's adapters.</summary>
internal static class Composition
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        var paths = AppPaths.ForCurrentPlatform();
        services.AddLogging(logging => logging.AddProvider(new FileLoggerProvider(paths.LogsFolder, TimeProvider.System)));
        AddCore(services, paths);
        if (OperatingSystem.IsWindows())
        {
            AddApplication(services, HotkeyNaming.Windows);
            AddWindows(services, paths);
        }
        else if (OperatingSystem.IsMacOS())
        {
            AddApplication(services, HotkeyNaming.MacOS);
            AddMacOS(services, paths);
        }
        else
        {
            throw new PlatformNotSupportedException("LocalTake runs on Windows and macOS.");
        }
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static void AddCore(ServiceCollection services, AppPaths paths)
    {
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<ISettingsStore>(sp =>
            new JsonSettingsStore(sp.GetRequiredService<IFileSystem>(), paths.SettingsFile, sp.GetRequiredService<SettingsDefaults>()));
        services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Load());
        services.AddSingleton<OutputPathAllocator>();
        services.AddSingleton<FileCounter>();
        services.AddSingleton<ImageSaver>();
        services.AddSingleton<CaptureDelivery>();
        services.AddSingleton<CaptureWorkflow>();
        services.AddSingleton(sp => AppPaths.CreateFfmpegLocator(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<IAudioDeviceLister, FfmpegAudioDeviceLister>();
        services.AddSingleton<IVideoRecorderFactory, FfmpegVideoRecorderFactory>();
        services.AddSingleton<DiagnosticsReport>();
        services.AddSingleton<IVideoFrames, FfmpegVideoFrames>();
        services.AddSingleton<IRecordingStatus>(sp => sp.GetRequiredService<RecordingSession>());
        services.AddSingleton(sp => new SettingsEditor(
            sp.GetRequiredService<AppSettings>(), sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<IFileSystem>(),
            OperatingSystem.IsMacOS() ? "/Users/moi/Captures" : @"C:\Users\Moi\Captures"));
    }

    /// <summary>The Avalonia adapters, the same on every platform.</summary>
    private static void AddApplication(ServiceCollection services, HotkeyNaming naming)
    {
        services.AddSingleton<IUiThread, DispatcherUiThread>();
        services.AddSingleton<IUserDialogs, Dialogs>();
        services.AddSingleton<INotifications, Toasts>();
        services.AddSingleton<IClipboard, ClipboardAdapter>();
        services.AddSingleton<IImageCodec, SkiaImageCodec>();
        services.AddSingleton<Monitors>();
        services.AddSingleton<DesktopScreen>();
        services.AddSingleton<IScreen>(sp => sp.GetRequiredService<DesktopScreen>());
        services.AddSingleton<IAreaPicker>(sp => sp.GetRequiredService<DesktopScreen>());
        services.AddSingleton<IRecordingViewFactory, RecordingViewFactory>();
        services.AddSingleton<EditorContext>();
        services.AddSingleton<IEditorLauncher, EditorLauncher>();
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<SettingsContext>(sp, naming));
        services.AddSingleton<Func<SettingsWindow>>(sp => () => new SettingsWindow(new SettingsViewModel(sp.GetRequiredService<SettingsContext>())));
        services.AddSingleton<VideoPreviewContext>();
        services.AddSingleton<Func<string, bool, VideoPreviewWindow>>(sp =>
            (path, fullScreen) => new VideoPreviewWindow(path, fullScreen, sp.GetRequiredService<VideoPreviewContext>()));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<TrayController>(sp, naming));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AddWindows(ServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(SettingsDefaults.ForWindows(paths.DefaultOutputFolder));
        services.AddSingleton<IFfmpegPlatform, DirectShowGdigrab>();
        services.AddSingleton<IScreenGrabber, WindowsScreenGrabber>();
        services.AddSingleton<INativeWindows, WindowsNativeWindows>();
        services.AddSingleton<INativeClipboard, WindowsClipboard>();
        services.AddSingleton<IShell, WindowsShell>();
        services.AddSingleton<IFileTrash, WindowsFileTrash>();
        services.AddSingleton<IStartupRegistration>(new WindowsStartupRegistration(
            WindowsStartupRegistration.RunKeyPath, "LocalTake", Environment.ProcessPath ?? "LocalTake.exe"));
        services.AddSingleton<IGlobalHotkeys>(sp => new WindowsHotkeys(sp.GetRequiredService<IUiThread>()));
        // The Start and Stop shortcuts of a recording live on their own window, registered only during a session.
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<RecordingSession>(
            sp, new WindowsHotkeys(sp.GetRequiredService<IUiThread>()), HotkeyNaming.Windows));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void AddMacOS(ServiceCollection services, AppPaths paths)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        services.AddSingleton(SettingsDefaults.ForMacOS(paths.DefaultOutputFolder));
        // AVFoundation numbers the displays from the main one, as "Capture screen 0", 1…
        services.AddSingleton<IFfmpegPlatform>(sp =>
        {
            var monitors = sp.GetRequiredService<Monitors>();
            return new AvFoundation(() => [.. monitors.All.OrderByDescending(s => s.IsPrimary).Select(s => s.Bounds.ToCore())]);
        });
        services.AddSingleton<IScreenGrabber, MacScreenGrabber>();
        services.AddSingleton<INativeWindows, MacNativeWindows>();
        services.AddSingleton<INativeClipboard, MacClipboard>();
        services.AddSingleton<IShell, MacShell>();
        services.AddSingleton<IFileTrash, MacFileTrash>();
        services.AddSingleton<IStartupRegistration>(new MacStartupRegistration(
            Path.Combine(home, "Library", "LaunchAgents"), Environment.ProcessPath ?? "LocalTake"));
        services.AddSingleton<IGlobalHotkeys>(sp => new MacHotkeys(sp.GetRequiredService<IUiThread>()));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<RecordingSession>(
            sp, new MacHotkeys(sp.GetRequiredService<IUiThread>()), HotkeyNaming.MacOS));
    }
}
