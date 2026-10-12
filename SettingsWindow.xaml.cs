using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LocalTake;

public partial class SettingsWindow : Window
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "LocalTake";

    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
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
        foreach (var box in HotkeyBoxes())
        {
            box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
            box.PreviewKeyUp += HotkeyBox_PreviewKeyUp;
        }

        StartupBox.IsChecked = IsStartupEnabled();
        UpdateQualityState();
    }

    private TextBox[] HotkeyBoxes() => new[] { HotkeyRegionBox, HotkeyWindowBox, HotkeyFullScreenBox, HotkeyLastRegionBox };

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
        else if (!Hotkey.IsModifierKey(key) && (modifiers != ModifierKeys.None || Hotkey.AllowedWithoutModifier(key)))
            box.Text = new Hotkey(modifiers, key).ToString();
    }

    /// <summary>Windows only reports Print Screen when it is released.</summary>
    private void HotkeyBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Snapshot)
            return;
        e.Handled = true;
        ((TextBox)sender).Text = new Hotkey(Keyboard.Modifiers, key).ToString();
    }

    private void DefaultHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        HotkeyRegionBox.Text = defaults.HotkeyRegion;
        HotkeyWindowBox.Text = defaults.HotkeyWindow;
        HotkeyFullScreenBox.Text = defaults.HotkeyFullScreen;
        HotkeyLastRegionBox.Text = defaults.HotkeyLastRegion;
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
        var example = ImageExporter.ExpandPattern(PatternBox.Text.Trim(), _settings.Counter, DateTime.Now);
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
        try
        {
            _settings.Save();
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
