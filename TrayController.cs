using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>Tray icon, global shortcuts and what happens to a capture.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Forms.NotifyIcon _tray;
    private readonly HotkeyManager _hotkeys = new();

    private readonly Forms.ToolStripMenuItem _regionItem;
    private readonly Forms.ToolStripMenuItem _windowItem;
    private readonly Forms.ToolStripMenuItem _fullScreenItem;
    private readonly Forms.ToolStripMenuItem _lastRegionItem;
    private readonly Forms.ToolStripMenuItem _videoRegionItem;
    private readonly Forms.ToolStripMenuItem _videoFullScreenItem;
    private readonly Forms.ToolStripMenuItem _stopVideoItem;

    private readonly VideoController _video;

    private SettingsWindow? _settingsWindow;
    private bool _capturing;
    private string? _lastSavedPath;

    public TrayController(AppSettings settings)
    {
        _settings = settings;
        _video = new VideoController(settings, OpenVideoPreview);

        var menu = new Forms.ContextMenuStrip();
        _regionItem = AddItem(menu, "Capturer une zone", () => CaptureInteractive(windowMode: false, fromMenu: true));
        _windowItem = AddItem(menu, "Capturer une fenêtre", () => CaptureInteractive(windowMode: true, fromMenu: true));
        _fullScreenItem = AddItem(menu, "Capturer tout l'écran", () => CaptureFullScreen(fromMenu: true));
        _lastRegionItem = AddItem(menu, "Capturer la dernière zone", () => CaptureLastRegion(fromMenu: true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        _videoRegionItem = AddItem(menu, "Enregistrer une vidéo (zone)", () => ToggleVideo(fullScreen: false, fromMenu: true));
        _videoFullScreenItem = AddItem(menu, "Enregistrer une vidéo (tout l'écran)", () => ToggleVideo(fullScreen: true, fromMenu: true));
        // "Démarrer" while the bar waits, "Arrêter" during the countdown or the recording.
        _stopVideoItem = AddItem(menu, "Arrêter l'enregistrement", () => _video.Toggle(fullScreen: false, fromMenu: true));
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

        _tray = new Forms.NotifyIcon
        {
            Icon = AppIcon.Icon,
            Text = "LocalTake",
            ContextMenuStrip = menu,
            Visible = true
        };
        // Left click opens the same menu as right click; NotifyIcon only exposes that through a private method.
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(_tray, null);
        };
        _tray.BalloonTipClicked += (_, _) => RevealLastSaved();
        _video.StateChanged += UpdateVideoState;

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

        Bind(_settings.HotkeyRegion, _regionItem, () => CaptureInteractive(windowMode: false, fromMenu: false));
        Bind(_settings.HotkeyWindow, _windowItem, () => CaptureInteractive(windowMode: true, fromMenu: false));
        Bind(_settings.HotkeyFullScreen, _fullScreenItem, () => CaptureFullScreen(fromMenu: false));
        Bind(_settings.HotkeyLastRegion, _lastRegionItem, () => CaptureLastRegion(fromMenu: false));
        Bind(_settings.HotkeyVideoRegion, _videoRegionItem, () => ToggleVideo(fullScreen: false, fromMenu: false));
        Bind(_settings.HotkeyVideoFullScreen, _videoFullScreenItem, () => ToggleVideo(fullScreen: true, fromMenu: false));
        return failures;
    }

    private static void ReportHotkeyFailures(List<string> failures)
    {
        MessageBox.Show(
            "Windows a refusé ces raccourcis, déjà utilisés par une autre application :\n\n  " +
            string.Join("\n  ", failures) +
            "\n\nCauses fréquentes : TinyTake (ou un autre outil de capture) est encore lancé, ou l'option Windows " +
            "« Utiliser la touche Impr. écran pour ouvrir la capture d'écran » est activée " +
            "(Paramètres > Accessibilité > Clavier).\n\nLes captures restent disponibles depuis l'icône de notification.",
            "LocalTake — raccourcis indisponibles", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- Captures -------------------------------------------------------------------------------------------

    private async void CaptureInteractive(bool windowMode, bool fromMenu)
    {
        await RunCapture(fromMenu, () =>
        {
            using var selection = SelectionOverlay.SelectArea(windowMode, out var area);
            if (selection is null)
                return null;

            _settings.LastRegion = new RegionRect(area.X, area.Y, area.Width, area.Height);
            _settings.TrySave();
            return ScreenCapture.ToBitmapSource(selection);
        });
    }

    private async void CaptureFullScreen(bool fromMenu)
    {
        await RunCapture(fromMenu, () =>
        {
            using var screen = ScreenCapture.Capture(ScreenCapture.VirtualScreen);
            return ScreenCapture.ToBitmapSource(screen);
        });
    }

    private async void CaptureLastRegion(bool fromMenu)
    {
        if (_settings.LastRegion is not { } last)
        {
            CaptureInteractive(windowMode: false, fromMenu);
            return;
        }

        await RunCapture(fromMenu, () =>
        {
            // Monitors may have changed since the region was chosen.
            var area = Rectangle.Intersect(new Rectangle(last.X, last.Y, last.Width, last.Height), ScreenCapture.VirtualScreen);
            if (area.Width < 1 || area.Height < 1)
                return null;
            using var bitmap = ScreenCapture.Capture(area);
            return ScreenCapture.ToBitmapSource(bitmap);
        });
    }

    private async Task RunCapture(bool fromMenu, Func<BitmapSource?> capture)
    {
        // The selection overlay or the editor would end up in the video.
        if (_capturing || _video.IsBusy)
            return;
        _capturing = true;
        try
        {
            // Let the tray menu fade out so that it does not appear in the capture.
            if (fromMenu)
                await Task.Delay(250);
            if (capture() is { } image)
                Deliver(image);
        }
        catch (Exception ex)
        {
            MessageBox.Show("La capture a échoué : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _capturing = false;
        }
    }

    private void Deliver(BitmapSource image)
    {
        switch (_settings.AfterCapture)
        {
            case AfterCaptureAction.OpenEditor:
                OpenEditor(image);
                break;
            case AfterCaptureAction.SaveToFile:
                NotifySaved(ImageExporter.SaveToOutputFolder(image, _settings));
                break;
            case AfterCaptureAction.CopyToClipboard:
                ImageExporter.CopyImage(image);
                ShowInfo("Capture copiée", "L'image est dans le presse-papier.");
                break;
            case AfterCaptureAction.SaveAndCopy:
                var path = ImageExporter.SaveToOutputFolder(image, _settings);
                // Copying the path, if enabled, must not replace the image in the clipboard.
                ImageExporter.CopyImage(image);
                NotifySaved(path);
                break;
        }
    }

    private void OpenEditor(BitmapSource image)
    {
        var editor = new EditorWindow(image, _settings);
        editor.Show();
        editor.Activate();
    }

    /// <summary>A balloon unrelated to a saved file: clicking it must not reveal an older capture.</summary>
    private void ShowInfo(string title, string text)
    {
        _lastSavedPath = null;
        _tray.ShowBalloonTip(2000, title, text, Forms.ToolTipIcon.None);
    }

    private void NotifySaved(string path)
    {
        _lastSavedPath = path;
        _tray.ShowBalloonTip(3000, "Capture enregistrée", path + "\nCliquez pour l'afficher dans l'Explorateur.",
            Forms.ToolTipIcon.None);
    }

    private void RevealLastSaved()
    {
        if (_lastSavedPath is not null && File.Exists(_lastSavedPath))
            Process.Start("explorer.exe", $"/select,\"{_lastSavedPath}\"");
    }

    // ---- Video ----------------------------------------------------------------------------------------------

    private void ToggleVideo(bool fullScreen, bool fromMenu)
    {
        if (!_capturing)
            _video.Toggle(fullScreen, fromMenu);
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
        if (_capturing || _video.IsBusy)
        {
            MessageBox.Show(preview, "Un enregistrement est déjà en préparation ou en cours.", "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!await preview.DiscardAsync() || _capturing || _video.IsBusy)
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
            OpenEditor(ScreenCapture.Normalize(image));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible d'ouvrir cette image : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Error);
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
        catch (Exception ex)
        {
            MessageBox.Show("Lecture du presse-papier impossible : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (image is null)
            ShowInfo("Presse-papier vide", "Le presse-papier ne contient pas d'image.");
        else
            OpenEditor(image);
    }

    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            Process.Start("explorer.exe", $"\"{_settings.OutputFolder}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible d'ouvrir le dossier des captures : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var answer = MessageBox.Show("Un enregistrement vidéo est en cours.\n\nL'arrêter et garder la vidéo avant de quitter ?",
                "LocalTake", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
                return;
            if (answer == MessageBoxResult.Yes)
                await _video.StopAsync();
        }
        // A recording prepared but not started, or a countdown, has nothing to keep.
        _video.Cancel();
        // A stop already under way (from the recording bar, for instance) is awaited rather than ignored.
        await _video.StopAsync();
        if (_video.IsBusy)
        {
            MessageBox.Show("Terminez d'abord la sélection de la zone à filmer.", "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
        _settingsWindow = new SettingsWindow(_settings);
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

    public void Dispose()
    {
        _video.Dispose();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
