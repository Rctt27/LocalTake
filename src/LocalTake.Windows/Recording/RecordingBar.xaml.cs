using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LocalTake.Adapters;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>
/// Audio toggles and Start before recording, then elapsed time, Stop and Cancel. Excluded from capture like
/// <see cref="RecordingFrame"/>.
/// </summary>
public partial class RecordingBar : Window
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly IFfmpegPlatform _platform;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _elapsed = new();
    private bool _hasMicrophone = true;
    private bool _hasSystemAudio;
    private bool _audioLocked;

    /// <summary>Set while the code updates the toggles, so that only the user's choices are saved.</summary>
    private bool _updatingToggles;

    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? CancelRequested;

    internal RecordingBar(AppSettings settings, ISettingsStore store, IFfmpegPlatform platform, INativeWindows windows)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        _platform = platform;
        // Known before ffmpeg lists the devices: on Windows it only takes an audio output.
        _hasSystemAudio = platform.CanRecordSystemAudio([]);
        SourceInitialized += (_, _) =>
            windows.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
        _timer.Tick += (_, _) =>
        {
            var elapsed = _elapsed.Elapsed;
            StatusText.Text = $"Enregistrement  {elapsed:mm\\:ss\\.f}";
            // The timer ticks for the tenths; the dot keeps blinking every half second.
            Dot.Opacity = (long)elapsed.TotalMilliseconds / 500 % 2 == 0 ? 1 : 0.35;
        };
        Closed += (_, _) => _timer.Stop();
        UpdateAudioToggles();
    }

    /// <summary>Below the recorded area when there is room, otherwise above it, otherwise inside it.</summary>
    /// <param name="area">Physical screen pixels.</param>
    public void ShowNear(PixelRect area)
    {
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(hwnd, out var r);
        var workArea = Forms.Screen.FromRectangle(area.ToRectangle()).WorkingArea.ToPixelRect();
        var position = RecordingBarPlacement.Place(area, new PixelSize(r.Right - r.Left, r.Bottom - r.Top), workArea);
        Native.SetWindowPos(hwnd, IntPtr.Zero, position.X, position.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
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

        var microphones = AudioDevices.Microphones(devices, _platform.SystemAudioDevice).ToList();
        _hasMicrophone = microphones.Count > 0;
        _hasSystemAudio = _platform.CanRecordSystemAudio(devices);

        // A list only when there is a choice to make, as in TinyTake.
        MicrophoneBox.SelectionChanged -= MicrophoneBox_SelectionChanged;
        MicrophoneBox.ItemsSource = microphones;
        MicrophoneBox.SelectedItem = AudioDevices.PickMicrophone(devices, _settings.Microphone, _platform.SystemAudioDevice);
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

    /// <summary>ffmpeg is launched but not capturing yet: Stop already works, the counter waits.</summary>
    public void ShowStarting()
    {
        StatusText.Text = "Démarrage…";
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
    }

    /// <summary>Capture has begun: what is said from now on is in the video.</summary>
    public void ShowRecording()
    {
        Dot.Opacity = 1;
        _elapsed.Start();
        StatusText.Text = "Enregistrement  00:00.0";
        // The bar sizes to its content: a counter a pixel wider or narrower would resize it every tenth and
        // clip the last digit while it redraws.
        StatusText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        StatusText.MinWidth = Math.Ceiling(StatusText.DesiredSize.Width) + 4;
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
            ? $"Nécessite {_platform.SystemAudioRequirement} : introuvable sur cet ordinateur."
            : _settings.RecordSystemAudio ? "Le son de l'ordinateur est enregistré. Cliquer pour le couper."
                : "Son de l'ordinateur coupé. Cliquer pour l'enregistrer.";
        SystemAudioLabel.TextDecorations = SystemAudioToggle.IsChecked == true ? null : TextDecorations.Strikethrough;
    }

    private void Microphone_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingToggles)
            return;
        _settings.RecordMicrophone = MicrophoneToggle.IsChecked == true;
        _store.TrySave(_settings);
        UpdateAudioToggles();
    }

    private void SystemAudio_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingToggles)
            return;
        _settings.RecordSystemAudio = SystemAudioToggle.IsChecked == true;
        _store.TrySave(_settings);
        UpdateAudioToggles();
    }

    private void MicrophoneBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneBox.SelectedItem is not string microphone)
            return;
        _settings.Microphone = microphone;
        _store.TrySave(_settings);
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
