using AppKit;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalTake.Mac;

/// <summary>Starts LocalTake as an icon in the menu bar, and owns that icon and the services for the app's life.</summary>
[Register(nameof(AppDelegate))]
internal sealed class AppDelegate : NSApplicationDelegate
{
    private ServiceProvider? _services;
    private NSStatusItem? _statusItem;

    private static string Version =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "";

    public override void DidFinishLaunching(NSNotification notification)
    {
        _services = Composition.Build();
        var log = _services.GetRequiredService<ILogger<AppDelegate>>();
        if (log.IsEnabled(LogLevel.Information))
            log.LogInformation("LocalTake {Version} started", Version);

        _statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Square);
        if (_statusItem.Button is { } button)
        {
            if (NSImage.ImageNamed("StatusIcon") is { } icon)
            {
                icon.Template = true; // drawn by macOS in the menu bar's colour, light or dark
                button.Image = icon;
            }
            button.ToolTip = "LocalTake";
        }
        _statusItem.Menu = BuildMenu();
    }

    public override void WillTerminate(NSNotification notification) => _services?.Dispose();

    private static NSMenu BuildMenu()
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        menu.AddItem(new NSMenuItem("LocalTake " + Version) { Enabled = false });
        menu.AddItem(NSMenuItem.SeparatorItem);
        menu.AddItem(new NSMenuItem("Quitter LocalTake", "q", (_, _) => NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication)));
        return menu;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _services?.Dispose();
            _statusItem?.Dispose();
        }
        base.Dispose(disposing);
    }
}
