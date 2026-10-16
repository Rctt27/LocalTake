using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using LocalTake.App.Adapters;
using LocalTake.App.Editor;
using LocalTake.App.Settings;
using LocalTake.App.Video;
using LocalTake.Core.Capture;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;

namespace LocalTake.App.Tray;

/// <summary>
/// The icon in the notification area (Windows) or the menu bar (macOS), its menu and the global shortcuts. The
/// captures themselves are the core's use cases.
/// </summary>
internal sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly CaptureWorkflow _captures;
    private readonly RecordingSession _video;
    private readonly IGlobalHotkeys _hotkeys;
    private readonly IEditorLauncher _editor;
    private readonly IUserDialogs _dialogs;
    private readonly INotifications _notifications;
    private readonly INativeClipboard _clipboard;
    private readonly IShell _shell;
    private readonly HotkeyNaming _naming;
    private readonly Func<SettingsWindow> _createSettingsWindow;
    private readonly Func<string, bool, VideoPreviewWindow> _createPreview;

    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _regionItem;
    private readonly NativeMenuItem _windowItem;
    private readonly NativeMenuItem _fullScreenItem;
    private readonly NativeMenuItem _lastRegionItem;
    private readonly NativeMenuItem _videoRegionItem;
    private readonly NativeMenuItem _videoFullScreenItem;
    private readonly NativeMenuItem _stopVideoItem;
    private readonly Dictionary<NativeMenuItem, string> _titles = [];

    private SettingsWindow? _settingsWindow;

    public TrayController(
        AppSettings settings, CaptureWorkflow captures, RecordingSession video, IGlobalHotkeys hotkeys, IEditorLauncher editor,
        IUserDialogs dialogs, INotifications notifications, INativeClipboard clipboard, IShell shell, HotkeyNaming naming,
        Func<SettingsWindow> createSettingsWindow, Func<string, bool, VideoPreviewWindow> createPreview)
    {
        _settings = settings;
        _captures = captures;
        _video = video;
        _hotkeys = hotkeys;
        _editor = editor;
        _dialogs = dialogs;
        _notifications = notifications;
        _clipboard = clipboard;
        _shell = shell;
        _naming = naming;
        _createSettingsWindow = createSettingsWindow;
        _createPreview = createPreview;

        var menu = new NativeMenu();
        _regionItem = Add(menu, "Capturer une zone", () => _ = _captures.CaptureAreaAsync(windowMode: false, fromMenu: true));
        _windowItem = Add(menu, "Capturer une fenêtre", () => _ = _captures.CaptureAreaAsync(windowMode: true, fromMenu: true));
        _fullScreenItem = Add(menu, "Capturer tout l'écran", () => _ = _captures.CaptureFullScreenAsync(fromMenu: true));
        _lastRegionItem = Add(menu, "Capturer la dernière zone", () => _ = _captures.CaptureLastRegionAsync(fromMenu: true));
        menu.Items.Add(new NativeMenuItemSeparator());
        _videoRegionItem = Add(menu, "Enregistrer une vidéo (zone)", () => ToggleVideo(fullScreen: false, fromMenu: true));
        _videoFullScreenItem = Add(menu, "Enregistrer une vidéo (tout l'écran)", () => ToggleVideo(fullScreen: true, fromMenu: true));
        // "Démarrer" while the bar waits, "Arrêter" during the countdown or the recording.
        _stopVideoItem = Add(menu, "Arrêter l'enregistrement", () => _ = _video.ToggleAsync(fullScreen: false, fromMenu: true));
        _stopVideoItem.IsVisible = false;
        menu.Items.Add(new NativeMenuItemSeparator());
        Add(menu, "Ouvrir une image…", () => _ = OpenImageFileAsync());
        Add(menu, "Coller depuis le presse-papier", PasteFromClipboard);
        menu.Items.Add(new NativeMenuItemSeparator());
        Add(menu, "Ouvrir le dossier des captures", OpenOutputFolder);
        Add(menu, "Réglages…", OpenSettings);
        menu.Items.Add(new NativeMenuItemSeparator());
        Add(menu, "Quitter", () => _ = QuitAsync());

        _icon = new TrayIcon { Icon = AppIcon.Window, ToolTipText = "LocalTake", Menu = menu, IsVisible = true };
        TrayIcon.SetIcons(Application.Current!, [_icon]);

        _video.StateChanged += (_, _) => UpdateVideoState();
        _video.VideoSaved += (_, e) => OpenVideoPreview(e.Path, e.FullScreen);

        var failures = RegisterHotkeys();
        if (failures.Count > 0)
            ReportHotkeyFailures(failures);
    }

    private NativeMenuItem Add(NativeMenu menu, string text, Action action)
    {
        var item = new NativeMenuItem(text);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        _titles[item] = text;
        return item;
    }

    /// <summary>Returns the descriptions of the shortcuts the system refused.</summary>
    private List<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failures = new List<string>();

        void Bind(string text, NativeMenuItem item, Action action)
        {
            // The shortcut is shown after the title: a native menu would treat a gesture as its own accelerator.
            item.Header = _titles[item];
            if (!Hotkey.TryParse(text, out var hotkey))
                return;
            var shown = hotkey.Format(_naming);
            item.Header = $"{_titles[item]}\t{shown}";
            if (!_hotkeys.Register(hotkey, action))
                failures.Add($"{shown}  ({_titles[item]})");
        }

        Bind(_settings.HotkeyRegion, _regionItem, () => _ = _captures.CaptureAreaAsync(windowMode: false, fromMenu: false));
        Bind(_settings.HotkeyWindow, _windowItem, () => _ = _captures.CaptureAreaAsync(windowMode: true, fromMenu: false));
        Bind(_settings.HotkeyFullScreen, _fullScreenItem, () => _ = _captures.CaptureFullScreenAsync(fromMenu: false));
        Bind(_settings.HotkeyLastRegion, _lastRegionItem, () => _ = _captures.CaptureLastRegionAsync(fromMenu: false));
        Bind(_settings.HotkeyVideoRegion, _videoRegionItem, () => ToggleVideo(fullScreen: false, fromMenu: false));
        Bind(_settings.HotkeyVideoFullScreen, _videoFullScreenItem, () => ToggleVideo(fullScreen: true, fromMenu: false));
        return failures;
    }

    private void ReportHotkeyFailures(List<string> failures) =>
        _dialogs.ShowWarning(
            "Le système a refusé ces raccourcis, déjà utilisés par une autre application :\n\n  " +
            string.Join("\n  ", failures) +
            (OperatingSystem.IsWindows()
                ? "\n\nCauses fréquentes : TinyTake (ou un autre outil de capture) est encore lancé, ou l'option Windows " +
                  "« Utiliser la touche Impr. écran pour ouvrir la capture d'écran » est activée " +
                  "(Paramètres > Accessibilité > Clavier)."
                : "\n\nChoisissez-en d'autres dans les réglages.") +
            "\n\nLes captures restent disponibles depuis le menu de l'icône LocalTake.");

    // ---- Video ----------------------------------------------------------------------------------------------

    private void ToggleVideo(bool fullScreen, bool fromMenu)
    {
        if (!_captures.IsCapturing)
            _ = _video.ToggleAsync(fullScreen, fromMenu);
    }

    /// <summary>As in TinyTake, a saved video opens in a preview rather than in a notification.</summary>
    private void OpenVideoPreview(string path, bool fullScreen)
    {
        var preview = _createPreview(path, fullScreen);
        preview.RetakeRequested += async (_, _) => await RetakeAsync(preview);
        preview.Show();
        preview.Activate();
    }

    /// <summary>"Refaire": discards the video, then records the same way again; an area is selected anew.</summary>
    private async Task RetakeAsync(VideoPreviewWindow preview)
    {
        // Toggle would act on the session under way instead: start its countdown, or stop it.
        if (_captures.IsCapturing || _video.IsBusy)
        {
            _dialogs.ShowInfo("Un enregistrement est déjà en préparation ou en cours.");
            return;
        }
        if (!await preview.DiscardAsync() || _captures.IsCapturing || _video.IsBusy)
            return;
        // As from the menu: the closed preview fades out before the screen is frozen for the selection.
        ToggleVideo(preview.FullScreen, fromMenu: true);
    }

    private void UpdateVideoState()
    {
        _stopVideoItem.IsVisible = _video.IsReady || _video.IsRecording;
        _stopVideoItem.Header = _video.IsReady ? "Démarrer l'enregistrement" : "Arrêter l'enregistrement";
        _videoRegionItem.IsEnabled = _videoFullScreenItem.IsEnabled = !_video.IsBusy;
        _icon.ToolTipText = _video.IsRecording ? "LocalTake — enregistrement en cours" : "LocalTake";
    }

    // ---- Other menu entries ---------------------------------------------------------------------------------

    private async Task OpenImageFileAsync()
    {
        var owner = Owner();
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ouvrir une image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
            SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(_settings.OutputFolder)
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;
        if (SkiaImageCodec.Load(path) is { } image)
            _editor.Open(new ImageData(image));
        else
            _dialogs.ShowError("Impossible d'ouvrir cette image : le format n'est pas reconnu.");
    }

    private void PasteFromClipboard()
    {
        PixelBuffer? image;
        try
        {
            image = _clipboard.GetImage();
        }
        catch (IOException ex)
        {
            _dialogs.ShowWarning("Lecture du presse-papier impossible : " + ex.Message);
            return;
        }

        if (image is null)
            _notifications.Info("Presse-papier vide", "Le presse-papier ne contient pas d'image.");
        else
            _editor.Open(new ImageData(image));
    }

    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            _shell.OpenFolder(_settings.OutputFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _dialogs.ShowWarning("Impossible d'ouvrir le dossier des captures : " + ex.Message);
        }
    }

    /// <summary>
    /// Closes the editors one by one first, so that each can ask whether to save its capture; quitting stops there if
    /// the user keeps one open.
    /// </summary>
    private async Task QuitAsync()
    {
        if (_video.IsRecording)
        {
            var answer = await _dialogs.AskYesNoCancelAsync("Un enregistrement vidéo est en cours.\n\nL'arrêter et garder la vidéo avant de quitter ?");
            if (answer == DialogAnswer.Cancel)
                return;
            if (answer == DialogAnswer.Yes)
                await _video.StopAsync();
        }
        // A recording prepared but not started, or a countdown, has nothing to keep.
        _video.Cancel();
        // A stop already under way (from the recording bar, for instance) is awaited rather than ignored.
        await _video.StopAsync();
        if (_video.IsBusy)
        {
            _dialogs.ShowInfo("Terminez d'abord la sélection de la zone à filmer.");
            return;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        foreach (var window in desktop.Windows.ToList())
        {
            if (window is EditorWindow editor)
            {
                if (!await editor.CloseAsync())
                    return;
            }
            else
            {
                window.Close();
            }
        }
        desktop.Shutdown();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        // While editing shortcuts, pressing one must reach the settings window instead of triggering a capture.
        _hotkeys.UnregisterAll();
        _settingsWindow = _createSettingsWindow();
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            var failures = RegisterHotkeys();
            if (failures.Count > 0)
                ReportHotkeyFailures(failures);
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>A window for the file dialogs, which need one.</summary>
    private static Window Owner() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { Windows.Count: > 0 } desktop
            ? desktop.Windows[0]
            : new Window();

    public void Dispose()
    {
        _hotkeys.UnregisterAll();
        _icon.IsVisible = false;
        _icon.Dispose();
    }
}
