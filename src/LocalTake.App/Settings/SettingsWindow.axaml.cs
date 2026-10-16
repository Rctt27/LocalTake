using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LocalTake.App.Adapters;
using LocalTake.Core.Hotkeys;

namespace LocalTake.App.Settings;

public partial class SettingsWindow : Window
{
    /// <summary>For the XAML previewer.</summary>
    public SettingsWindow() => InitializeComponent();

    internal SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        Icon = AppIcon.Window;
        DataContext = viewModel;
        viewModel.Finished += (_, _) => Close();
        Opened += async (_, _) => await viewModel.LoadAudioDevicesAsync();

        foreach (var box in HotkeyGrid.Children.OfType<TextBox>())
        {
            box.IsReadOnly = true;
            box.AddHandler(KeyDownEvent, HotkeyBox_KeyDown, RoutingStrategies.Tunnel);
            box.AddHandler(KeyUpEvent, HotkeyBox_KeyUp, RoutingStrategies.Tunnel);
        }
        BrowseFolderButton.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Dossier des captures",
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(viewModel.OutputFolder)
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                viewModel.OutputFolder = path;
        };
        BrowseFfmpegButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "ffmpeg", AllowMultiple = false });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                viewModel.FfmpegPath = path;
                await viewModel.LoadAudioDevicesAsync();
            }
        };
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

    /// <summary>Records the combination pressed; Backspace or Delete alone clears it.</summary>
    private void HotkeyBox_KeyDown(object? sender, KeyEventArgs e)
    {
        // Tab, Enter and Escape keep their usual role: move on, save, cancel.
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Tab or Key.Enter or Key.Escape)
            return;

        e.Handled = true;
        var box = (TextBox)sender!;
        if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
            box.Text = "";
        else if (!AvaloniaKeys.IsModifier(e.Key) &&
                 new Hotkey(AvaloniaKeys.ToModifiers(e.KeyModifiers), AvaloniaKeys.ToKeyCode(e.Key)) is { IsValid: true } hotkey)
            box.Text = hotkey.Format(ViewModel.Naming);
    }

    /// <summary>Windows only reports Print Screen when it is released.</summary>
    private void HotkeyBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.PrintScreen)
            return;
        e.Handled = true;
        ((TextBox)sender!).Text = new Hotkey(AvaloniaKeys.ToModifiers(e.KeyModifiers), KeyCode.PrintScreen).Format(ViewModel.Naming);
    }
}

internal static class SettingsConverters
{
    /// <summary>Red when ffmpeg is missing, grey otherwise.</summary>
    public static readonly IValueConverter StatusBrush =
        new FuncValueConverter<bool, IBrush>(missing => missing ? Brushes.IndianRed : Brushes.Gray);
}
