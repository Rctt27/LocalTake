using AppKit;
using Foundation;
using LocalTake.Core.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalTake.Mac;

/// <summary>Starts LocalTake as an icon in the menu bar (<see cref="StatusMenu"/>), and owns the services for the app's life.</summary>
[Register(nameof(AppDelegate))]
internal sealed class AppDelegate : NSApplicationDelegate
{
    private ServiceProvider? _services;

    private static string Version =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "";

    public override void DidFinishLaunching(NSNotification notification)
    {
        _services = Composition.Build();
        var log = _services.GetRequiredService<ILogger<AppDelegate>>();
        if (log.IsEnabled(LogLevel.Information))
            log.LogInformation("LocalTake {Version} started", Version);

        // The microphone tracks of a previous run, closed before they were transcribed.
        _services.GetRequiredService<MicrophoneTracks>().DeleteLeftovers();
        _services.GetRequiredService<StatusMenu>();
    }

    /// <summary>Quitting stops a recording and asks about every capture not saved yet; "Annuler" keeps LocalTake running.</summary>
    public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) =>
        _services?.GetService<StatusMenu>()?.ShouldTerminate() ?? NSApplicationTerminateReply.Now;

    public override void WillTerminate(NSNotification notification) => _services?.Dispose();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _services?.Dispose();
        base.Dispose(disposing);
    }
}
