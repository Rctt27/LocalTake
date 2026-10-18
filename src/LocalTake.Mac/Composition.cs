using System.IO.Abstractions;
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
using LocalTake.Mac.Adapters;
using LocalTake.Mac.Capture;
using LocalTake.Mac.Editor;
using LocalTake.Mac.Platform;
using LocalTake.Mac.Recording;
using LocalTake.Mac.Settings;
using LocalTake.Platform.MacOS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalTake.Mac;

/// <summary>The composition root: the core's use cases with the macOS adapters.</summary>
internal static class Composition
{
    public static ServiceProvider Build()
    {
        var paths = AppPaths.ForCurrentPlatform();
        var defaults = SettingsDefaults.ForMacOS(paths.DefaultOutputFolder);
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.AddProvider(new FileLoggerProvider(paths.LogsFolder, TimeProvider.System)));
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton(defaults);
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(sp.GetRequiredService<IFileSystem>(), paths.SettingsFile, defaults));
        services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Load());

        // Core
        services.AddSingleton<OutputPathAllocator>();
        services.AddSingleton<FileCounter>();
        services.AddSingleton<ImageSaver>();
        services.AddSingleton<CaptureDelivery>();
        services.AddSingleton<CaptureWorkflow>();
        services.AddSingleton(sp => AppPaths.CreateFfmpegLocator(sp.GetRequiredService<IFileSystem>()));
        // The screens recorded by ffmpeg are wired in with the recording.
        // ffmpeg numbers the screens in NSScreen's order.
        services.AddSingleton<IFfmpegPlatform>(sp => new AvFoundation(sp.GetRequiredService<IDisplays>().Screens));
        // Start and Stop are registered apart from the capture shortcuts, only while a recording is being prepared or made.
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<RecordingSession>(sp,
            (IGlobalHotkeys)new MacHotkeys(sp.GetRequiredService<IUiThread>()), HotkeyNaming.MacOS));
        services.AddSingleton<IRecordingStatus>(sp => sp.GetRequiredService<RecordingSession>());

        // Infrastructure
        services.AddSingleton<IAudioDeviceLister, FfmpegAudioDeviceLister>();
        services.AddSingleton<IVideoRecorderFactory, FfmpegVideoRecorderFactory>();
        services.AddSingleton<DiagnosticsReport>();

        // macOS adapters
        services.AddSingleton<IUiThread>(new MainThread());
        services.AddSingleton<IDisplays, MacDisplays>();
        services.AddSingleton<IRecordingViewFactory, MacRecordingViewFactory>();
        services.AddSingleton<IScreenGrabber, MacScreenGrabber>();
        services.AddSingleton<INativeWindows, MacNativeWindows>();
        services.AddSingleton<IClipboard, MacClipboard>();
        services.AddSingleton<IImageCodec, MacImageCodec>();
        services.AddSingleton<IShell, MacShell>();
        services.AddSingleton<IFileTrash, MacFileTrash>();
        services.AddSingleton<IStartupRegistration, MacStartupRegistration>();
        services.AddSingleton<IGlobalHotkeys>(sp => new MacHotkeys(sp.GetRequiredService<IUiThread>()));
        services.AddSingleton<MacScreen>();
        services.AddSingleton<IScreen>(sp => sp.GetRequiredService<MacScreen>());
        services.AddSingleton<IAreaPicker>(sp => sp.GetRequiredService<MacScreen>());
        services.AddSingleton<IUserDialogs, MacDialogs>();
        services.AddSingleton<INotifications, MacNotifications>();
        services.AddSingleton(sp => new EditorContext(sp.GetRequiredService<AppSettings>(), sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<ImageSaver>(), sp.GetRequiredService<IClipboard>(), sp.GetRequiredService<IShell>()));
        services.AddSingleton<MacEditorLauncher>();
        services.AddSingleton<IEditorLauncher>(sp => sp.GetRequiredService<MacEditorLauncher>());
        services.AddSingleton(sp => new SettingsEditor(sp.GetRequiredService<AppSettings>(), sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IFileSystem>(), "/Users/moi/Captures"));
        services.AddSingleton<SettingsContext>();
        services.AddSingleton<Func<SettingsWindowController>>(sp => () => new SettingsWindowController(sp.GetRequiredService<SettingsContext>()));
        services.AddSingleton<StatusMenu>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
