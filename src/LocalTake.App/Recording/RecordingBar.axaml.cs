using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LocalTake.App.Capture;

using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;

namespace LocalTake.App.Recording;

/// <summary>
/// Audio toggles and Start before recording, then elapsed time, Stop and Cancel. Excluded from capture like the frame
/// around the area.
/// </summary>
public partial class RecordingBar : Window
{
    private readonly AppSettings _settings = null!;
    private readonly ISettingsStore _store = null!;
    private readonly IFfmpegPlatform _platform = null!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _elapsed = new();
    private bool _hasMicrophone = true;
    private bool _hasSystemAudio = true;
    private bool _audioLocked;

    /// <summary>Set while the code updates the toggles, so that only the user's choices are saved.</summary>
    private bool _updatingToggles;

    /// <summary>For the XAML previewer.</summary>
    public RecordingBar() => InitializeComponent();

    internal RecordingBar(AppSettings settings, ISettingsStore store, IFfmpegPlatform platform, INativeWindows nativeWindows)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        _platform = platform;

        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is { } handle)
                nativeWindows.ExcludeFromCapture(handle);
        };
        DragArea.PointerPressed += (_, e) => BeginMoveDrag(e);
        _timer.Tick += (_, _) =>
        {
            var elapsed = _elapsed.Elapsed;
            StatusText.Text = "Enregistrement  " + elapsed.ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture);
            // The timer ticks for the tenths; the dot keeps blinking every half second.
            Dot.Opacity = (long)elapsed.TotalMilliseconds / 500 % 2 == 0 ? 1 : 0.35;
        };
        Closed += (_, _) => _timer.Stop();

        MicrophoneToggle.IsCheckedChanged += (_, _) => OnToggle(() => _settings.RecordMicrophone = MicrophoneToggle.IsChecked == true);
        SystemAudioToggle.IsCheckedChanged += (_, _) => OnToggle(() => _settings.RecordSystemAudio = SystemAudioToggle.IsChecked == true);
        MicrophoneBox.SelectionChanged += (_, _) =>
        {
            if (_updatingToggles || MicrophoneBox.SelectedItem is not string microphone)
                return;
            _settings.Microphone = microphone;
            _store.TrySave(_settings);
        };
        StartButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        StopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        CancelButton.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        UpdateAudioToggles();
    }

    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? CancelRequested;

    /// <summary>Below the recorded area when there is room, otherwise above it, otherwise inside it.</summary>
    internal void ShowNear(Core.Geometry.PixelRect area, Core.Geometry.PixelRect workArea)
    {
        Opened += (_, _) =>
        {
            var size = new PixelSize((int)Math.Ceiling(Bounds.Width * RenderScaling), (int)Math.Ceiling(Bounds.Height * RenderScaling));
            var position = RecordingBarPlacement.Place(area, new Core.Geometry.PixelSize(size.Width, size.Height), workArea);
            Position = new Avalonia.PixelPoint(position.X, position.Y);
        };
        Show();
    }

    public void ShowReady(string? startShortcut, string? stopShortcut)
    {
        Dot.Opacity = 0.35;
        StatusText.Text = "Prêt à enregistrer";
        StartButton.Content = startShortcut is null ? "●  Démarrer" : $"●  Démarrer ({startShortcut})";
        StopButton.Content = stopShortcut is null ? "■  Arrêter" : $"■  Arrêter ({stopShortcut})";
        StartButton.IsVisible = true;
        StopButton.IsVisible = false;
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
        _hasSystemAudio = devices.Contains(_platform.SystemAudioDevice);

        _updatingToggles = true;
        try
        {
            // A list only when there is a choice to make, as in TinyTake.
            MicrophoneBox.ItemsSource = microphones;
            MicrophoneBox.SelectedItem = AudioDevices.PickMicrophone(devices, _settings.Microphone, _platform.SystemAudioDevice);
        }
        finally
        {
            _updatingToggles = false;
        }
        UpdateAudioToggles();
    }

    public void ShowCountdown(int seconds)
    {
        StatusText.Text = $"Début dans {seconds}…";
        StartButton.IsEnabled = false;
        _audioLocked = true;
        UpdateAudioToggles();
    }

    public void ShowStarting()
    {
        StatusText.Text = "Démarrage…";
        StartButton.IsVisible = false;
        StopButton.IsVisible = true;
    }

    public void ShowRecording()
    {
        Dot.Opacity = 1;
        _elapsed.Start();
        StatusText.Text = "Enregistrement  00:00.0";
        _timer.Start();
    }

    public void ShowFinishing()
    {
        _timer.Stop();
        Dot.Opacity = 0.35;
        StatusText.Text = "Finalisation…";
        StopButton.IsEnabled = CancelButton.IsEnabled = false;
    }

    private void OnToggle(Action save)
    {
        if (_updatingToggles)
            return;
        save();
        _store.TrySave(_settings);
        UpdateAudioToggles();
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
            MicrophoneToggle.IsChecked = _hasMicrophone && _settings.RecordMicrophone;
            MicrophoneToggle.IsEnabled = _hasMicrophone && !_audioLocked;
            ToolTip.SetTip(MicrophoneToggle, !_hasMicrophone ? "Aucun micro détecté"
                : _settings.RecordMicrophone ? "Le micro est enregistré. Cliquer pour le couper." : "Micro coupé. Cliquer pour l'enregistrer.");
            MicrophoneLabel.TextDecorations = MicrophoneToggle.IsChecked == true ? null : TextDecorations.Strikethrough;
            MicrophoneBox.IsVisible = MicrophoneToggle.IsChecked == true && MicrophoneBox.ItemCount > 1;
            MicrophoneBox.IsEnabled = !_audioLocked;

            SystemAudioToggle.IsChecked = _hasSystemAudio && _settings.RecordSystemAudio;
            SystemAudioToggle.IsEnabled = _hasSystemAudio && !_audioLocked;
            ToolTip.SetTip(SystemAudioToggle, !_hasSystemAudio
                ? $"Nécessite {_platform.SystemAudioRequirement} : introuvable sur cet ordinateur."
                : _settings.RecordSystemAudio ? "Le son de l'ordinateur est enregistré. Cliquer pour le couper."
                    : "Son de l'ordinateur coupé. Cliquer pour l'enregistrer.");
            SystemAudioLabel.TextDecorations = SystemAudioToggle.IsChecked == true ? null : TextDecorations.Strikethrough;
        }
        finally
        {
            _updatingToggles = false;
        }
    }
}

/// <summary>
/// A red outline just outside the recorded area, made of four thin windows: nothing covers the area itself, so the
/// outline needs no click-through and cannot appear in the video.
/// </summary>
internal sealed class RecordingFrame : IDisposable
{
    private const int Thickness = 3;
    private readonly List<Window> _strips = [];

    public RecordingFrame(Core.Geometry.PixelRect area, INativeWindows nativeWindows)
    {
        Core.Geometry.PixelRect[] strips =
        [
            new(area.X - Thickness, area.Y - Thickness, area.Width + 2 * Thickness, Thickness),
            new(area.X - Thickness, area.Bottom, area.Width + 2 * Thickness, Thickness),
            new(area.X - Thickness, area.Y, Thickness, area.Height),
            new(area.Right, area.Y, Thickness, area.Height)
        ];
        foreach (var strip in strips)
        {
            var window = new Window
            {
                SystemDecorations = SystemDecorations.None,
                Topmost = true,
                ShowActivated = false,
                ShowInTaskbar = false,
                CanResize = false,
                Background = new SolidColorBrush(Color.FromRgb(229, 57, 53)),
                Position = new Avalonia.PixelPoint(strip.X, strip.Y)
            };
            window.Opened += (_, _) =>
            {
                window.Width = strip.Width / window.RenderScaling;
                window.Height = strip.Height / window.RenderScaling;
                window.Position = new Avalonia.PixelPoint(strip.X, strip.Y);
                if (window.TryGetPlatformHandle()?.Handle is { } handle)
                    nativeWindows.ExcludeFromCapture(handle);
            };
            window.Width = strip.Width;
            window.Height = strip.Height;
            _strips.Add(window);
        }
    }

    public void Show()
    {
        foreach (var strip in _strips)
            strip.Show();
    }

    public void Dispose()
    {
        foreach (var strip in _strips)
            strip.Close();
        _strips.Clear();
    }
}

/// <summary>The bar and, for an area, the frame around it.</summary>
internal sealed class RecordingView : IRecordingView
{
    private readonly RecordingBar _bar;
    private RecordingFrame? _frame;

    public RecordingView(RecordingBar bar, RecordingFrame? frame, Core.Geometry.PixelRect area, Core.Geometry.PixelRect workArea)
    {
        _bar = bar;
        _frame = frame;
        _frame?.Show();
        _bar.StartRequested += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _bar.StopRequested += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        _bar.CancelRequested += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _bar.ShowNear(area, workArea);
    }

    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? CancelRequested;

    public void ShowReady(string? startShortcut, string? stopShortcut) => _bar.ShowReady(startShortcut, stopShortcut);
    public void SetAudioDevices(IReadOnlyCollection<string> devices) => _bar.SetAudioDevices(devices);
    public void ShowCountdown(int seconds) => _bar.ShowCountdown(seconds);
    public void ShowStarting() => _bar.ShowStarting();
    public void ShowRecording() => _bar.ShowRecording();

    /// <summary>Nothing more is recorded: the frame goes at once.</summary>
    public void ShowFinishing()
    {
        _bar.ShowFinishing();
        _frame?.Dispose();
        _frame = null;
    }

    public void Dispose()
    {
        _frame?.Dispose();
        _frame = null;
        _bar.Close();
    }
}

internal sealed class RecordingViewFactory(
    AppSettings settings, ISettingsStore store, IFfmpegPlatform platform, INativeWindows nativeWindows, Monitors monitors) : IRecordingViewFactory
{
    public IRecordingView Show(Core.Geometry.PixelRect area, bool fullScreen) => new RecordingView(
        new RecordingBar(settings, store, platform, nativeWindows),
        fullScreen ? null : new RecordingFrame(area, nativeWindows),
        area,
        monitors.WorkAreaFor(area));
}
