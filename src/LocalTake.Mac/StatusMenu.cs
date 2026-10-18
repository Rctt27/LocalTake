using AppKit;
using Foundation;
using LocalTake.Core.Capture;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;
using LocalTake.Mac.Adapters;
using LocalTake.Mac.Platform;
using LocalTake.Platform.MacOS;

namespace LocalTake.Mac;

/// <summary>
/// LocalTake's icon in the menu bar, its menu and the global shortcuts, as the tray icon on Windows; the captures
/// themselves are the core's use cases.
/// </summary>
internal sealed class StatusMenu : IDisposable
{
    private readonly AppSettings _settings;
    private readonly CaptureWorkflow _captures;
    private readonly IGlobalHotkeys _hotkeys;
    private readonly IUserDialogs _dialogs;
    private readonly IShell _shell;
    private readonly IEditorLauncher _editor;
    private readonly INotifications _notifications;

    private readonly NSStatusItem _statusItem;
    private readonly NSMenuItem _regionItem;
    private readonly NSMenuItem _windowItem;
    private readonly NSMenuItem _fullScreenItem;
    private readonly NSMenuItem _lastRegionItem;

    public StatusMenu(AppSettings settings, CaptureWorkflow captures, IGlobalHotkeys hotkeys, IUserDialogs dialogs, IShell shell,
        IEditorLauncher editor, INotifications notifications)
    {
        _settings = settings;
        _captures = captures;
        _hotkeys = hotkeys;
        _dialogs = dialogs;
        _shell = shell;
        _editor = editor;
        _notifications = notifications;

        var menu = new NSMenu { AutoEnablesItems = false };
        menu.AddItem(new NSMenuItem("LocalTake " + Version) { Enabled = false });
        menu.AddItem(NSMenuItem.SeparatorItem);
        _regionItem = AddItem(menu, "Capturer une zone", () => _ = _captures.CaptureAreaAsync(windowMode: false, fromMenu: true));
        _windowItem = AddItem(menu, "Capturer une fenêtre", () => _ = _captures.CaptureAreaAsync(windowMode: true, fromMenu: true));
        _fullScreenItem = AddItem(menu, "Capturer tout l'écran", () => _ = _captures.CaptureFullScreenAsync(fromMenu: true));
        _lastRegionItem = AddItem(menu, "Capturer la dernière zone", () => _ = _captures.CaptureLastRegionAsync(fromMenu: true));
        menu.AddItem(NSMenuItem.SeparatorItem);
        AddItem(menu, "Ouvrir une image…", OpenImageFile);
        AddItem(menu, "Coller depuis le presse-papiers", PasteFromClipboard);
        menu.AddItem(NSMenuItem.SeparatorItem);
        AddItem(menu, "Ouvrir le dossier des captures", OpenOutputFolder);
        AddItem(menu, "Réglages…", () => { }).Enabled = false;
        menu.AddItem(NSMenuItem.SeparatorItem);
        AddItem(menu, "Quitter LocalTake", () => NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication));

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
        _statusItem.Menu = menu;

        var failures = RegisterHotkeys();
        if (failures.Count > 0)
            ReportHotkeyFailures(failures);
    }

    private static string Version =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "";

    private static NSMenuItem AddItem(NSMenu menu, string title, Action action)
    {
        var item = new NSMenuItem(title, (_, _) => action());
        menu.AddItem(item);
        return item;
    }

    /// <summary>Returns the descriptions of the shortcuts macOS refused.</summary>
    private List<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failures = new List<string>();

        void Bind(string text, NSMenuItem item, Action action)
        {
            item.KeyEquivalent = "";
            if (!Hotkey.TryParse(text, out var hotkey))
                return;
            // Shown in the menu, as macOS shows its own shortcuts.
            if (MacKeys.KeyEquivalent(hotkey.Key) is { } equivalent)
            {
                item.KeyEquivalent = equivalent;
                item.KeyEquivalentModifierMask = Mask(hotkey.Modifiers);
            }
            if (!_hotkeys.Register(hotkey, action))
                failures.Add($"{hotkey.Format(HotkeyNaming.MacOS)}  ({item.Title})");
        }

        Bind(_settings.HotkeyRegion, _regionItem, () => _ = _captures.CaptureAreaAsync(windowMode: false, fromMenu: false));
        Bind(_settings.HotkeyWindow, _windowItem, () => _ = _captures.CaptureAreaAsync(windowMode: true, fromMenu: false));
        Bind(_settings.HotkeyFullScreen, _fullScreenItem, () => _ = _captures.CaptureFullScreenAsync(fromMenu: false));
        Bind(_settings.HotkeyLastRegion, _lastRegionItem, () => _ = _captures.CaptureLastRegionAsync(fromMenu: false));
        return failures;
    }

    private static NSEventModifierMask Mask(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Control) ? NSEventModifierMask.ControlKeyMask : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Alt) ? NSEventModifierMask.AlternateKeyMask : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Shift) ? NSEventModifierMask.ShiftKeyMask : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Meta) ? NSEventModifierMask.CommandKeyMask : 0);

    private void ReportHotkeyFailures(List<string> failures) =>
        _dialogs.ShowWarning(
            "macOS a refusé ces raccourcis, déjà utilisés par une autre application ou absents d'un clavier de Mac :\n\n  " +
            string.Join("\n  ", failures) +
            "\n\nLes captures restent disponibles depuis l'icône de LocalTake dans la barre des menus.");

    private void OpenImageFile()
    {
        if (MacDialogs.AskImageToOpen(_settings.OutputFolder) is not { } path)
            return;
        if (MacImage.Load(path) is { } image)
            _editor.Open(image);
        else
            _dialogs.ShowError("Impossible d'ouvrir cette image : macOS ne sait pas la lire.");
    }

    private void PasteFromClipboard()
    {
        if (MacClipboard.GetImage() is { } image)
            _editor.Open(image);
        else
            _notifications.Info("Presse-papiers vide", "Le presse-papiers ne contient pas d'image.");
    }

    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            _shell.OpenFolder(_settings.OutputFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowWarning("Impossible d'ouvrir le dossier des captures : " + ex.Message);
        }
    }

    public void Dispose()
    {
        _hotkeys.UnregisterAll();
        NSStatusBar.SystemStatusBar.RemoveStatusItem(_statusItem);
        _statusItem.Dispose();
    }
}
