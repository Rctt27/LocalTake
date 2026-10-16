using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalTake.Core.Files;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>What the settings window needs from the rest of the app.</summary>
internal sealed record SettingsContext(
    AppSettings Settings, ISettingsStore Store, SettingsDefaults Defaults, FfmpegLocator Locator, IFfmpegPlatform Platform,
    IAudioDeviceLister AudioDevices);

public partial class SettingsWindow : Window
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "LocalTake";

    private readonly AppSettings _settings;
    private readonly SettingsContext _context;

    internal SettingsWindow(SettingsContext context)
    {
        InitializeComponent();
        _context = context;
        _settings = context.Settings;
        var settings = _settings;
        Icon = AppIcon.ImageSource;

        FolderBox.Text = settings.OutputFolder;
        PatternBox.Text = settings.FileNamePattern;
        FormatBox.SelectedItem = FormatBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.Format)
                                 ?? FormatBox.Items[0];
        QualitySlider.Value = settings.JpegQuality;
        CopyPathBox.IsChecked = settings.CopyPathAfterSave;
        AfterCaptureBox.SelectedItem = AfterCaptureBox.Items.Cast<ComboBoxItem>()
            .First(i => (string)i.Tag == settings.AfterCapture.ToString());

        HotkeyRegionBox.Text = settings.HotkeyRegion;
        HotkeyWindowBox.Text = settings.HotkeyWindow;
        HotkeyFullScreenBox.Text = settings.HotkeyFullScreen;
        HotkeyLastRegionBox.Text = settings.HotkeyLastRegion;
        HotkeyVideoRegionBox.Text = settings.HotkeyVideoRegion;
        HotkeyVideoFullScreenBox.Text = settings.HotkeyVideoFullScreen;
        HotkeyVideoStartBox.Text = settings.HotkeyVideoStart;
        HotkeyVideoStopBox.Text = settings.HotkeyVideoStop;
        foreach (var box in HotkeyBoxes())
        {
            box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
            box.PreviewKeyUp += HotkeyBox_PreviewKeyUp;
        }

        StartupBox.IsChecked = IsStartupEnabled();
        UpdateQualityState();

        FfmpegBox.Text = settings.FfmpegPath;
        FrameRateBox.ItemsSource = FrameRates;
        FrameRateBox.SelectedItem = FrameRates.OrderBy(f => Math.Abs(f - settings.VideoFrameRate)).First();
        RecordMicrophoneBox.IsChecked = settings.RecordMicrophone;
        SystemAudioBox.IsChecked = settings.RecordSystemAudio;
        FfmpegBox.TextChanged += (_, _) => UpdateFfmpegStatus();
        Loaded += (_, _) => LoadAudioDevices();

        MaxHeight = SystemParameters.WorkArea.Height * 0.95;
    }

    private static readonly int[] FrameRates = { 15, 24, 30, 60 };

    private int _audioLoadGeneration;

    private string? UpdateFfmpegStatus()
    {
        var path = _context.Locator.Find(FfmpegBox.Text.Trim());
        FfmpegStatus.Text = path is not null
            ? (string.IsNullOrWhiteSpace(FfmpegBox.Text) ? "Détecté : " + path : "ffmpeg trouvé.")
            : "ffmpeg introuvable : les vidéos ne pourront pas être enregistrées. " + _context.Platform.InstallHint;
        FfmpegStatus.Foreground = path is not null ? System.Windows.Media.Brushes.DimGray : System.Windows.Media.Brushes.Firebrick;
        return path;
    }

    /// <summary>Asks ffmpeg for the audio inputs; the computer sound needs TinyTake's driver.</summary>
    private async void LoadAudioDevices()
    {
        // A newer load (Browse clicked while ffmpeg was still listing) wins; the older one must not fill the list too.
        var generation = ++_audioLoadGeneration;
        // Keep the choice being edited, not only the saved one.
        var selected = (MicrophoneBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.Microphone;

        var ffmpeg = UpdateFfmpegStatus();
        IReadOnlyList<string> devices = [];
        if (ffmpeg is not null)
            devices = await _context.AudioDevices.ListAsync(ffmpeg);

        if (generation != _audioLoadGeneration)
            return;

        MicrophoneBox.Items.Clear();
        MicrophoneBox.Items.Add(new ComboBoxItem { Content = "Premier micro détecté", Tag = "" });
        MicrophoneBox.SelectedIndex = 0;

        // A chosen microphone stays selectable even if it is unplugged right now.
        var microphones = AudioDevices.Microphones(devices, _context.Platform.SystemAudioDevice).ToList();
        if (!string.IsNullOrEmpty(selected) && !microphones.Contains(selected))
            microphones.Add(selected);
        foreach (var name in microphones)
        {
            var item = new ComboBoxItem { Content = name, Tag = name };
            MicrophoneBox.Items.Add(item);
            if (name == selected)
                MicrophoneBox.SelectedItem = item;
        }

        var hasSystemAudio = devices.Contains(_context.Platform.SystemAudioDevice);
        SystemAudioBox.IsEnabled = hasSystemAudio || SystemAudioBox.IsChecked == true;
        SystemAudioBox.ToolTip = hasSystemAudio
            ? "Utilise le pilote « virtual-audio-capturer » installé avec TinyTake (TinyTake Filter)."
            : "Nécessite le pilote « virtual-audio-capturer », installé avec TinyTake (TinyTake Filter) : introuvable sur cet ordinateur.";
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|Programmes|*.exe", FileName = "ffmpeg.exe" };
        if (dialog.ShowDialog(this) != true)
            return;
        FfmpegBox.Text = dialog.FileName;
        LoadAudioDevices();
    }

    private TextBox[] HotkeyBoxes() => new[]
    {
        HotkeyRegionBox, HotkeyWindowBox, HotkeyFullScreenBox, HotkeyLastRegionBox, HotkeyVideoRegionBox, HotkeyVideoFullScreenBox, HotkeyVideoStartBox, HotkeyVideoStopBox
    };

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // Tab, Enter and Escape keep their usual role: move on, save, cancel.
        if (modifiers == ModifierKeys.None && key is Key.Tab or Key.Enter or Key.Escape)
            return;

        e.Handled = true;
        var box = (TextBox)sender;
        if (key is Key.Back or Key.Delete && modifiers == ModifierKeys.None)
            box.Text = "";
        else if (!WpfKeyMap.IsModifierKey(key) && new Hotkey(WpfKeyMap.ToModifiers(modifiers), WpfKeyMap.ToKeyCode(key)) is { IsValid: true } hotkey)
            box.Text = hotkey.ToString();
    }

    /// <summary>Windows only reports Print Screen when it is released.</summary>
    private void HotkeyBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Snapshot)
            return;
        e.Handled = true;
        ((TextBox)sender).Text = new Hotkey(WpfKeyMap.ToModifiers(Keyboard.Modifiers), KeyCode.PrintScreen).ToString();
    }

    private void DefaultHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var defaults = _context.Defaults;
        HotkeyRegionBox.Text = defaults.HotkeyRegion;
        HotkeyWindowBox.Text = defaults.HotkeyWindow;
        HotkeyFullScreenBox.Text = defaults.HotkeyFullScreen;
        HotkeyLastRegionBox.Text = defaults.HotkeyLastRegion;
        HotkeyVideoRegionBox.Text = defaults.HotkeyVideoRegion;
        HotkeyVideoFullScreenBox.Text = defaults.HotkeyVideoFullScreen;
        HotkeyVideoStartBox.Text = defaults.HotkeyVideoStart;
        HotkeyVideoStopBox.Text = defaults.HotkeyVideoStop;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Dossier des captures",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(FolderBox.Text) ? FolderBox.Text : ""
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
            FolderBox.Text = dialog.SelectedPath;
    }

    private void Pattern_TextChanged(object sender, TextChangedEventArgs e)
    {
        var example = FileNamePattern.Expand(PatternBox.Text.Trim(), _settings.Counter, DateTime.Now);
        PatternPreview.Text = $"Exemple : {example}.{SelectedFormat()}   ·   Variables : %YYYY% %MM% %DD% %hh% %mm% %ss% %NUM%";
    }

    private void Format_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateQualityState();
        if (PatternPreview is not null)
            Pattern_TextChanged(this, null!);
    }

    private string SelectedFormat() => (FormatBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "png";

    private void UpdateQualityState()
    {
        if (QualitySlider is null)
            return;
        var isJpeg = SelectedFormat() == "jpg";
        QualitySlider.IsEnabled = isJpeg;
        QualityLabel.Opacity = isJpeg ? 1 : 0.5;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var hotkeys = HotkeyBoxes().Select(b => b.Text.Trim()).Where(t => t.Length > 0).ToList();
        var duplicate = hotkeys.GroupBy(h => h, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(this, $"Le raccourci {duplicate.Key} est attribué deux fois.", "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(FolderBox.Text) || string.IsNullOrWhiteSpace(PatternBox.Text))
        {
            MessageBox.Show(this, "Le dossier et le nom des fichiers sont obligatoires.", "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ffmpegPath = FfmpegBox.Text.Trim();
        if (ffmpegPath.Length > 0 && !File.Exists(ffmpegPath))
        {
            MessageBox.Show(this, "Le fichier ffmpeg indiqué n'existe pas. Laissez le champ vide pour une détection automatique.",
                "LocalTake", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // A relative path would depend on the working directory, which is System32 when Windows starts the app.
        var folder = FolderBox.Text.Trim();
        if (!Path.IsPathFullyQualified(folder))
        {
            MessageBox.Show(this, "Indiquez le chemin complet du dossier, par exemple C:\\Users\\Moi\\Captures.", "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ce dossier est inutilisable : " + ex.Message, "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.OutputFolder = folder;
        _settings.FileNamePattern = PatternBox.Text.Trim();
        _settings.Format = SelectedFormat();
        _settings.JpegQuality = (int)QualitySlider.Value;
        _settings.CopyPathAfterSave = CopyPathBox.IsChecked == true;
        _settings.AfterCapture = Enum.Parse<AfterCaptureAction>((string)((ComboBoxItem)AfterCaptureBox.SelectedItem).Tag);
        _settings.HotkeyRegion = HotkeyRegionBox.Text.Trim();
        _settings.HotkeyWindow = HotkeyWindowBox.Text.Trim();
        _settings.HotkeyFullScreen = HotkeyFullScreenBox.Text.Trim();
        _settings.HotkeyLastRegion = HotkeyLastRegionBox.Text.Trim();
        _settings.HotkeyVideoRegion = HotkeyVideoRegionBox.Text.Trim();
        _settings.HotkeyVideoFullScreen = HotkeyVideoFullScreenBox.Text.Trim();
        _settings.HotkeyVideoStart = HotkeyVideoStartBox.Text.Trim();
        _settings.HotkeyVideoStop = HotkeyVideoStopBox.Text.Trim();
        _settings.FfmpegPath = ffmpegPath;
        _settings.Microphone = (MicrophoneBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _settings.Microphone;
        _settings.RecordMicrophone = RecordMicrophoneBox.IsChecked == true;
        _settings.RecordSystemAudio = SystemAudioBox.IsChecked == true;
        _settings.VideoFrameRate = (int)FrameRateBox.SelectedItem;
        try
        {
            _context.Store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Les réglages n'ont pas pu être enregistrés : " + ex.Message, "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetStartup(StartupBox.IsChecked == true);
        Close();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        else if (key.GetValue(RunValueName) is not null)
            key.DeleteValue(RunValueName);
    }
}
