using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
/// The status, which is the bar's on/off button (▶ to start, then the elapsed time with ■ to stop), the audio and
/// subtitle toggles, and Cancel. Excluded from capture like
/// <see cref="RecordingFrame"/>.
/// </summary>
public partial class RecordingBar : Window
{
    /// <summary>What the status button does: start, stop, or nothing while the countdown runs or the file is finished.</summary>
    private enum StatusMode { Start, Wait, Stop }

    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly IFfmpegPlatform _platform;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _elapsed = new();
    private bool _hasMicrophone = true;
    private bool _hasSystemAudio;
    private readonly bool _canTranscribe;
    private bool _audioLocked;
    private StatusMode _mode = StatusMode.Wait;
    private string _startHint = "Démarrer";
    private string _stopHint = "Arrêter";
    private string _startTip = "";
    private string _stopTip = "";

    /// <summary>Set while the code updates the toggles, so that only the user's choices are saved.</summary>
    private bool _updatingToggles;

    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? CancelRequested;

    /// <param name="canTranscribe">Whether there is a model to transcribe the microphone with.</param>
    internal RecordingBar(AppSettings settings, ISettingsStore store, IFfmpegPlatform platform, INativeWindows windows, bool canTranscribe)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        _platform = platform;
        _canTranscribe = canTranscribe;
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
            StatusText.Text = Elapsed(_elapsed.Elapsed);
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

    /// <param name="startShortcut">Shown beside "Prêt à enregistrer", or null when it could not be registered.</param>
    /// <param name="stopShortcut">Shown beside the elapsed time, or null.</param>
    public void ShowReady(string? startShortcut, string? stopShortcut)
    {
        string WithShortcut(string text, string? shortcut) => shortcut is null ? text : $"{text} ({shortcut})";
        _startHint = WithShortcut("Démarrer", startShortcut);
        _stopHint = WithShortcut("Arrêter", stopShortcut);
        _startTip = WithShortcut("Lancer le compte à rebours de 3 secondes, puis l'enregistrement", startShortcut);
        _stopTip = WithShortcut("Arrêter l'enregistrement et garder la vidéo", stopShortcut);
        SetMode(StatusMode.Start);
        StatusText.Text = "Prêt à enregistrer";
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
        SetMode(StatusMode.Wait);
        StatusText.Text = $"Début dans {seconds}…";
        _audioLocked = true;
        UpdateAudioToggles();
    }

    /// <summary>ffmpeg is launched but not capturing yet: Stop already works, the counter waits.</summary>
    public void ShowStarting()
    {
        SetMode(StatusMode.Stop);
        StatusText.Text = "Démarrage…";
    }

    /// <summary>Capture has begun: what is said from now on is in the video.</summary>
    /// <remarks>Usually straight from the countdown: ffmpeg captures before 0, without <see cref="ShowStarting"/>.</remarks>
    public void ShowRecording()
    {
        SetMode(StatusMode.Stop);
        _elapsed.Start();
        StatusText.Text = Elapsed(TimeSpan.Zero);
        // The bar sizes to its content: a counter a pixel wider or narrower would resize it every tenth and
        // clip the last digit while it redraws.
        StatusText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        StatusText.MinWidth = Math.Ceiling(StatusText.DesiredSize.Width) + 4;
        _timer.Start();
    }

    /// <summary>During the recording: the trigger to hold to highlight, or null when there is none.</summary>
    public void ShowHighlightShortcut(string? shortcut)
    {
        HighlightHint.Text = shortcut is null ? "" : $"Surligner : maintenir {shortcut}";
        HighlightHint.Visibility = shortcut is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowFinishing()
    {
        _timer.Stop();
        HighlightHint.Visibility = Visibility.Collapsed;
        SetMode(StatusMode.Wait);
        CancelButton.IsEnabled = false;
        // Still the stop square, dimmed: the recording is over, its file is being finished.
        Dot.Visibility = Visibility.Collapsed;
        StopSquare.Visibility = Visibility.Visible;
        StopSquare.Opacity = 0.35;
        StatusText.Text = "Finalisation…";
    }

    /// <summary>
    /// The status button: ▶ and its shortcut when ready, ■ and its shortcut from "Démarrage…" until the end, a dimmed
    /// dot and no click while the countdown runs.
    /// </summary>
    private void SetMode(StatusMode mode)
    {
        _mode = mode;
        StatusButton.IsEnabled = mode != StatusMode.Wait;
        PlayIcon.Visibility = mode == StatusMode.Start ? Visibility.Visible : Visibility.Collapsed;
        StopSquare.Visibility = mode == StatusMode.Stop ? Visibility.Visible : Visibility.Collapsed;
        StopSquare.Opacity = 1;
        Dot.Visibility = mode == StatusMode.Wait ? Visibility.Visible : Visibility.Collapsed;
        StatusHint.Visibility = mode == StatusMode.Wait ? Visibility.Collapsed : Visibility.Visible;
        StatusHint.Text = mode == StatusMode.Start ? _startHint : _stopHint;
        StatusButton.ToolTip = mode == StatusMode.Start ? _startTip : _stopTip;
    }

    private static string Elapsed(TimeSpan elapsed) => elapsed.ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture);

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

        var transcription = SubtitleToggle.For(_canTranscribe, MicrophoneToggle.IsChecked == true, _settings.TranscribeMicrophone, _audioLocked);
        TranscriptionToggle.IsChecked = transcription.IsChecked;
        TranscriptionToggle.IsEnabled = transcription.IsEnabled;
        TranscriptionToggle.ToolTip = transcription.ToolTip;
        TranscriptionLabel.TextDecorations = TranscriptionToggle.IsChecked == true ? null : TextDecorations.Strikethrough;
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

    private void Transcription_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingToggles)
            return;
        _settings.TranscribeMicrophone = TranscriptionToggle.IsChecked == true;
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

    private void Status_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == StatusMode.Start)
            StartRequested?.Invoke();
        else if (_mode == StatusMode.Stop)
            StopRequested?.Invoke();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
