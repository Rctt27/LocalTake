using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using LocalTake.Adapters;
using LocalTake.Core.Capture;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>The tray menu and the global shortcuts; the captures themselves are the core's use cases.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly CaptureWorkflow _captures;
    private readonly RecordingSession _video;
    private readonly IGlobalHotkeys _hotkeys;
    private readonly TrayIcon _tray;
    private readonly IEditorLauncher _editor;
    private readonly IUserDialogs _dialogs;
    private readonly Func<SettingsWindow> _createSettingsWindow;

    private readonly Forms.ToolStripMenuItem _regionItem;
    private readonly Forms.ToolStripMenuItem _windowItem;
    private readonly Forms.ToolStripMenuItem _fullScreenItem;
    private readonly Forms.ToolStripMenuItem _lastRegionItem;
    private readonly Forms.ToolStripMenuItem _videoRegionItem;
    private readonly Forms.ToolStripMenuItem _videoFullScreenItem;
    private readonly Forms.ToolStripMenuItem _stopVideoItem;

    private SettingsWindow? _settingsWindow;

    public TrayController(
        AppSettings settings, CaptureWorkflow captures, RecordingSession video, IGlobalHotkeys hotkeys, TrayIcon tray,
        IEditorLauncher editor, IUserDialogs dialogs, Func<SettingsWindow> createSettingsWindow)
    {
        _settings = settings;
        _captures = captures;
        _video = video;
        _hotkeys = hotkeys;
        _tray = tray;
        _editor = editor;
        _dialogs = dialogs;
        _createSettingsWindow = createSettingsWindow;

        var menu = tray.Menu;
        _regionItem = AddItem(menu, "Capturer une zone", () => _ = _captures.CaptureAreaAsync(windowMode: false, fromMenu: true));
        _windowItem = AddItem(menu, "Capturer une fenêtre", () => _ = _captures.CaptureAreaAsync(windowMode: true, fromMenu: true));
        _fullScreenItem = AddItem(menu, "Capturer tout l'écran", () => _ = _captures.CaptureFullScreenAsync(fromMenu: true));
        _lastRegionItem = AddItem(menu, "Capturer la dernière zone", () => _ = _captures.CaptureLastRegionAsync(fromMenu: true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        _videoRegionItem = AddItem(menu, "Enregistrer une vidéo (zone)", () => ToggleVideo(fullScreen: false, fromMenu: true));
        _videoFullScreenItem = AddItem(menu, "Enregistrer une vidéo (tout l'écran)", () => ToggleVideo(fullScreen: true, fromMenu: true));
        // "Démarrer" while the bar waits, "Arrêter" during the countdown or the recording.
        _stopVideoItem = AddItem(menu, "Arrêter l'enregistrement", () => _ = _video.ToggleAsync(fullScreen: false, fromMenu: true));
        _stopVideoItem.Font = new Font(_stopVideoItem.Font, System.Drawing.FontStyle.Bold);
        _stopVideoItem.Visible = false;
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem(menu, "Ouvrir une image…", OpenImageFile);
        AddItem(menu, "Coller depuis le presse-papier", PasteFromClipboard);
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem(menu, "Ouvrir le dossier des captures", OpenOutputFolder);
        AddItem(menu, "Réglages…", OpenSettings);
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem(menu, "Quitter", Quit);

        _video.StateChanged += (_, _) => UpdateVideoState();
        _video.VideoSaved += (_, e) => OpenVideoPreview(e.Path, e.FullScreen);

        var failures = RegisterHotkeys();
        if (failures.Count > 0)
            ReportHotkeyFailures(failures);
    }

    private static Forms.ToolStripMenuItem AddItem(Forms.ContextMenuStrip menu, string text, Action action)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    /// <summary>Returns the descriptions of the shortcuts Windows refused.</summary>
    private List<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failures = new List<string>();

        void Bind(string text, Forms.ToolStripMenuItem item, Action action)
        {
            item.ShortcutKeyDisplayString = null;
            if (!Hotkey.TryParse(text, out var hotkey))
                return;
            item.ShortcutKeyDisplayString = hotkey.ToString();
            if (!_hotkeys.Register(hotkey, action))
                failures.Add($"{hotkey}  ({item.Text})");
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
            "Windows a refusé ces raccourcis, déjà utilisés par une autre application :\n\n  " +
            string.Join("\n  ", failures) +
            "\n\nCauses fréquentes : TinyTake (ou un autre outil de capture) est encore lancé, ou l'option Windows " +
            "« Utiliser la touche Impr. écran pour ouvrir la capture d'écran » est activée " +
            "(Paramètres > Accessibilité > Clavier).\n\nLes captures restent disponibles depuis l'icône de notification.");

    // ---- Video ----------------------------------------------------------------------------------------------

    private void ToggleVideo(bool fullScreen, bool fromMenu)
    {
        if (!_captures.IsCapturing)
            _ = _video.ToggleAsync(fullScreen, fromMenu);
    }

    /// <summary>As in TinyTake, a saved video opens in a player rather than in a notification.</summary>
    private void OpenVideoPreview(string path, bool fullScreen)
    {
        var preview = new VideoPreviewWindow(path, fullScreen);
        preview.RetakeRequested += () => Retake(preview, fullScreen);
        preview.Show();
        preview.Activate();
    }

    /// <summary>"Refaire": discards the video, then records the same way again; an area is selected anew.</summary>
    private async void Retake(VideoPreviewWindow preview, bool fullScreen)
    {
        // Toggle would act on the session under way instead: start its countdown, or stop it.
        if (_captures.IsCapturing || _video.IsBusy)
        {
            MessageBox.Show(preview, "Un enregistrement est déjà en préparation ou en cours.", "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!await preview.DiscardAsync() || _captures.IsCapturing || _video.IsBusy)
            return;
        // As from the menu: the closed preview fades out before the screen is frozen for the selection.
        ToggleVideo(fullScreen, fromMenu: true);
    }

    private void UpdateVideoState()
    {
        // From the area selection on, a preview still playing would end up in the new video's sound.
        if (_video.IsBusy)
            foreach (var preview in Application.Current.Windows.OfType<VideoPreviewWindow>())
                preview.PauseIfPlaying();

        _stopVideoItem.Visible = _video.IsReady || _video.IsRecording;
        _stopVideoItem.Text = _video.IsReady ? "Démarrer l'enregistrement" : "Arrêter l'enregistrement";
        _videoRegionItem.Enabled = _videoFullScreenItem.Enabled = !_video.IsBusy;
        _tray.Text = _video.IsRecording ? "LocalTake — enregistrement en cours" : "LocalTake";
    }

    // ---- Other menu entries ---------------------------------------------------------------------------------

    private void OpenImageFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Tous les fichiers|*.*",
            InitialDirectory = Directory.Exists(_settings.OutputFolder) ? _settings.OutputFolder : null
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(dialog.FileName);
            image.EndInit();
            _editor.Open(new WpfImage(ScreenCapture.Normalize(image)));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException)
        {
            _dialogs.ShowError("Impossible d'ouvrir cette image : " + ex.Message);
        }
    }

    private void PasteFromClipboard()
    {
        BitmapSource? image;
        try
        {
            image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
            if (image is not null)
                image = ScreenCapture.Normalize(image);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _dialogs.ShowWarning("Lecture du presse-papier impossible : " + ex.Message);
            return;
        }

        if (image is null)
            _tray.Info("Presse-papier vide", "Le presse-papier ne contient pas d'image.");
        else
            _editor.Open(new WpfImage(image));
    }

    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            Process.Start("explorer.exe", $"\"{_settings.OutputFolder}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _dialogs.ShowWarning("Impossible d'ouvrir le dossier des captures : " + ex.Message);
        }
    }

    /// <summary>
    /// Closes the editors one by one first: during Application.Shutdown, cancelling the "save before closing?"
    /// prompt would be ignored and the capture lost.
    /// </summary>
    private async void Quit()
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

        foreach (var window in Application.Current.Windows.OfType<Window>().ToList())
        {
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Close();
            if (!closed)
                return;
        }
        Application.Current.Shutdown();
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

    public void Dispose() => _hotkeys.UnregisterAll();
}

/// <summary>Opens each capture in its own editor window.</summary>
internal sealed class WpfEditorLauncher(EditorContext context) : IEditorLauncher
{
    public void Open(IImage image)
    {
        var editor = new EditorWindow(WpfImage.Unwrap(image), context);
        editor.Show();
        editor.Activate();
    }
}
