using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>
/// Audio toggles and Start before recording, then elapsed time, Stop and Cancel. Excluded from capture like
/// <see cref="RecordingFrame"/>.
/// </summary>
public partial class RecordingBar : Window
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stopwatch _elapsed = new();
    private bool _hasMicrophone = true;
    private bool _hasSystemAudio = true;
    private bool _audioLocked;

    /// <summary>Set while the code updates the toggles, so that only the user's choices are saved.</summary>
    private bool _updatingToggles;

    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? CancelRequested;

    internal RecordingBar(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        SourceInitialized += (_, _) =>
            Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
        _timer.Tick += (_, _) =>
        {
            StatusText.Text = $"Enregistrement  {_elapsed.Elapsed:mm\\:ss}";
            Dot.Opacity = Dot.Opacity < 1 ? 1 : 0.35;
        };
        Closed += (_, _) => _timer.Stop();
        UpdateAudioToggles();
    }

    /// <summary>Below the recorded area when there is room, otherwise above it, otherwise inside it.</summary>
    /// <param name="area">Physical screen pixels.</param>
    public void ShowNear(Drawing.Rectangle area)
    {
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(hwnd, out var r);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        var screen = Forms.Screen.FromRectangle(area).WorkingArea;
        const int gap = 10;

        var x = Math.Clamp(area.X + (area.Width - width) / 2, screen.Left, Math.Max(screen.Left, screen.Right - width));
        int y;
        if (area.Bottom + gap + height <= screen.Bottom)
            y = area.Bottom + gap;
        else if (area.Top - gap - height >= screen.Top)
            y = area.Top - gap - height;
        else
            y = screen.Bottom - height - gap;

        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <param name="startShortcut">Shown on the Start button, or null when it could not be registered.</param>
    /// <param name="stopShortcut">Shown on the Stop button, or null.</param>
    public void ShowReady(string? startShortcut, string? stopShortcut)
    {
        Dot.Opacity = 0.35;
        StatusText.Text = "Prêt à enregistrer";
        StartButton.Content = startShortcut is null ? "●  Démarrer" : $"●  Démarrer ({startShortcut})";
        StopButton.Content = stopShortcut is null ? "■  Arrêter" : $"■  Arrêter ({stopShortcut})";
        StartButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        StartButton.IsEnabled = true;
        _audioLocked = false;
        UpdateAudioToggles();
    }

    /// <summary>Enables what this computer can record, once ffmpeg has listed the audio inputs.</summary>
    public void SetAudioDevices(IReadOnlyCollection<string> devices)
    {
        // An empty list means ffmpeg could not list them: the saved choices are then left as they are.
        if (devices.Count == 0)
            return;

        var microphones = Ffmpeg.Microphones(devices).ToList();
        _hasMicrophone = microphones.Count > 0;
        _hasSystemAudio = devices.Contains(Ffmpeg.SystemAudioDevice);

        // A list only when there is a choice to make, as in TinyTake.
        MicrophoneBox.SelectionChanged -= MicrophoneBox_SelectionChanged;
        MicrophoneBox.ItemsSource = microphones;
        MicrophoneBox.SelectedItem = Ffmpeg.PickMicrophone(devices, _settings.Microphone);
        MicrophoneBox.SelectionChanged += MicrophoneBox_SelectionChanged;
        UpdateAudioToggles();
    }

    public void ShowCountdown(int seconds)
    {
        StatusText.Text = $"Début dans {seconds}…";
        StartButton.IsEnabled = false;
        _audioLocked = true;
        UpdateAudioToggles();
    }

    public void ShowRecording()
    {
        Dot.Opacity = 1;
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        _elapsed.Start();
        StatusText.Text = "Enregistrement  00:00";
        _timer.Start();
    }

    public void ShowFinishing()
    {
        _timer.Stop();
        Dot.Opacity = 0.35;
        StatusText.Text = "Finalisation…";
        StopButton.IsEnabled = CancelButton.IsEnabled = false;
    }

    /// <summary>
    /// The toggles show the saved choice, except for an input this computer lacks. Once the countdown starts, ffmpeg's
    /// inputs are set: the toggles are locked, as in TinyTake.
    /// </summary>
    private void UpdateAudioToggles()
    {
        _updatingToggles = true;
        try
        {
            ApplyAudioToggles();
        }
        finally
        {
            _updatingToggles = false;
        }
    }

    private void ApplyAudioToggles()
    {
        MicrophoneToggle.IsChecked = _hasMicrophone && _settings.RecordMicrophone;
        MicrophoneToggle.IsEnabled = _hasMicrophone && !_audioLocked;
        MicrophoneToggle.ToolTip = !_hasMicrophone ? "Aucun micro détecté"
            : _settings.RecordMicrophone ? "Le micro est enregistré. Cliquer pour le couper." : "Micro coupé. Cliquer pour l'enregistrer.";
        MicrophoneLabel.TextDecorations = MicrophoneToggle.IsChecked == true ? null : TextDecorations.Strikethrough;

        MicrophoneBox.Visibility = MicrophoneToggle.IsChecked == true && MicrophoneBox.Items.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        MicrophoneBox.IsEnabled = !_audioLocked;

        SystemAudioToggle.IsChecked = _hasSystemAudio && _settings.RecordSystemAudio;
        SystemAudioToggle.IsEnabled = _hasSystemAudio && !_audioLocked;
        SystemAudioToggle.ToolTip = !_hasSystemAudio
            ? "Nécessite le pilote « virtual-audio-capturer », installé avec TinyTake (TinyTake Filter) : introuvable sur cet ordinateur."
            : _settings.RecordSystemAudio ? "Le son de l'ordinateur est enregistré. Cliquer pour le couper."
                : "Son de l'ordinateur coupé. Cliquer pour l'enregistrer.";
        SystemAudioLabel.TextDecorations = SystemAudioToggle.IsChecked == true ? null : TextDecorations.Strikethrough;
    }

    private void Microphone_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingToggles)
            return;
        _settings.RecordMicrophone = MicrophoneToggle.IsChecked == true;
        _settings.TrySave();
        UpdateAudioToggles();
    }

    private void SystemAudio_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingToggles)
            return;
        _settings.RecordSystemAudio = SystemAudioToggle.IsChecked == true;
        _settings.TrySave();
        UpdateAudioToggles();
    }

    private void MicrophoneBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneBox.SelectedItem is not string microphone)
            return;
        _settings.Microphone = microphone;
        _settings.TrySave();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
