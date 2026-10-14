using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace LocalTake;

/// <summary>
/// One screen recording at a time: area selection, then a bar to choose the audio and start, a 3-second
/// countdown, ffmpeg, and the MP4 in the output folder.
/// </summary>
internal sealed class VideoController : IDisposable
{
    private enum State { Idle, Selecting, Ready, Countdown, Recording, Finishing }

    private readonly AppSettings _settings;
    /// <summary>The saved video, and whether it was full screen rather than an area.</summary>
    private readonly Action<string, bool> _onSaved;

    /// <summary>Start and Stop shortcuts, registered only while a recording is prepared or under way.</summary>
    private readonly HotkeyManager _sessionHotkeys = new();

    private State _state = State.Idle;
    private RecordingFrame? _frame;
    private RecordingBar? _bar;
    private VideoRecorder? _recorder;
    private Task _stopping = Task.CompletedTask;

    private string _ffmpeg = "";
    private Rectangle _target;
    private bool _fullScreen;
    private Task<List<string>> _audioDevices = Task.FromResult(new List<string>());
    private string? _startShortcut;
    private string? _stopShortcut;

    /// <summary>Incremented when a countdown is abandoned, so that its pending loop does not start the recording.</summary>
    private int _session;

    /// <summary>The %NUM% value taken by the current video, given back if nothing is kept.</summary>
    private int? _reservedNumber;

    public VideoController(AppSettings settings, Action<string, bool> onSaved)
    {
        _settings = settings;
        _onSaved = onSaved;
    }

    /// <summary>From selection to the end of finalization; image captures wait meanwhile.</summary>
    public bool IsBusy => _state != State.Idle;

    /// <summary>The area is chosen and the bar waits for Start.</summary>
    public bool IsReady => _state == State.Ready;

    public bool IsRecording => _state is State.Countdown or State.Recording;

    public event Action? StateChanged;

    /// <summary>The video shortcut chooses the area, then starts the countdown, then stops the recording.</summary>
    public async void Toggle(bool fullScreen, bool fromMenu)
    {
        switch (_state)
        {
            case State.Ready:
                StartCountdown();
                return;
            case State.Countdown or State.Recording:
                await StopAsync();
                return;
            case State.Selecting or State.Finishing:
                return;
        }

        var ffmpeg = Ffmpeg.Find(_settings.FfmpegPath);
        if (ffmpeg is null)
        {
            MessageBox.Show("ffmpeg est introuvable : LocalTake s'en sert pour encoder les vidéos.\n\n" + Ffmpeg.InstallHint,
                "LocalTake", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetState(State.Selecting);
        Rectangle? selected = null;
        try
        {
            // Let the tray menu fade out before freezing the screen.
            if (fromMenu)
                await Task.Delay(250);
            if (fullScreen)
            {
                selected = ScreenCapture.VirtualScreen;
            }
            else
            {
                using var frozen = SelectionOverlay.SelectArea(windowMode: false, out var area);
                if (frozen is not null)
                    selected = area;
            }
        }
        finally
        {
            if (selected is null)
                SetState(State.Idle);
        }
        if (selected is not { } chosen)
            return;

        // H.264 in yuv420p needs even dimensions.
        _target = new Rectangle(chosen.X, chosen.Y, Math.Max(2, chosen.Width & ~1), Math.Max(2, chosen.Height & ~1));
        _fullScreen = fullScreen;
        _ffmpeg = ffmpeg;
        // Listed while the user looks at the bar: the toggles then show what this computer can record.
        _audioDevices = Ffmpeg.ListAudioDevicesAsync(ffmpeg);

        if (!fullScreen)
        {
            _frame = new RecordingFrame(_target);
            _frame.Show();
        }
        var bar = _bar = new RecordingBar(_settings);
        bar.StartRequested += StartCountdown;
        bar.StopRequested += async () => await StopAsync();
        bar.CancelRequested += Cancel;
        RegisterSessionHotkeys();
        bar.ShowReady(_startShortcut, _stopShortcut);
        bar.ShowNear(_target);
        SetState(State.Ready);

        var devices = await _audioDevices;
        if (_bar == bar)
            bar.SetAudioDevices(devices);
    }

    /// <summary>Keeps the shortcuts Windows accepted, to show them on the bar.</summary>
    private void RegisterSessionHotkeys()
    {
        string? Bind(string text, Action action) =>
            Hotkey.TryParse(text, out var hotkey) && _sessionHotkeys.Register(hotkey, action) ? hotkey.ToString() : null;

        _startShortcut = Bind(_settings.HotkeyVideoStart, () =>
        {
            if (_state == State.Ready)
                StartCountdown();
        });
        _stopShortcut = Bind(_settings.HotkeyVideoStop, () =>
        {
            if (_state is State.Countdown or State.Recording)
                _ = StopAsync();
        });
    }

    /// <summary>Start button or shortcut: 3, 2, 1, then ffmpeg starts.</summary>
    private async void StartCountdown()
    {
        if (_state != State.Ready || _bar is not { } bar)
            return;
        var session = ++_session;
        SetState(State.Countdown);
        bar.ShowCountdown(3);

        // Usually listed long ago; at worst ffmpeg's own timeout applies.
        var devices = await _audioDevices;
        for (var seconds = 3; seconds > 0; seconds--)
        {
            if (session != _session || _state != State.Countdown)
                return;
            bar.ShowCountdown(seconds);
            await Task.Delay(1000);
        }
        if (session != _session || _state != State.Countdown)
            return;

        VideoRecorder started;
        try
        {
            var microphone = _settings.RecordMicrophone ? Ffmpeg.PickMicrophone(devices, _settings.Microphone) : null;
            var systemAudio = _settings.RecordSystemAudio && (devices.Count == 0 || devices.Contains(Ffmpeg.SystemAudioDevice));
            var options = new VideoOptions(Math.Clamp(_settings.VideoFrameRate, 5, 60), microphone, systemAudio);
            var path = ImageExporter.NextOutputPath(_settings, ".mp4");
            ReserveNumber();
            _recorder = started = VideoRecorder.Start(_ffmpeg, _target, path, options,
                recorder => Application.Current.Dispatcher.BeginInvoke(() => OnUnexpectedExit(recorder)));
        }
        catch (Exception ex)
        {
            ReleaseNumber();
            CloseUi();
            SetState(State.Idle);
            MessageBox.Show("ffmpeg n'a pas pu démarrer : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Recording from now on, so that Stop finishes the file; but the devices take about 0.3 s to open, and the
        // counter only starts once they capture, so that the first words are not lost.
        // Should ffmpeg never announce it, the counter starts anyway rather than leave the bar waiting.
        bar.ShowStarting();
        SetState(State.Recording);
        await Task.WhenAny(started.Capturing, Task.Delay(TimeSpan.FromSeconds(5)));
        if (_recorder == started && _bar == bar && _state == State.Recording)
            bar.ShowRecording();
    }

    /// <summary>
    /// Ends the recording and keeps the video. During the countdown nothing is recorded yet: the bar goes back to
    /// waiting for Start. While a stop is already in progress, returns that one.
    /// </summary>
    public Task StopAsync()
    {
        switch (_state)
        {
            case State.Countdown when _bar is { } bar:
                _session++;
                bar.ShowReady(_startShortcut, _stopShortcut);
                SetState(State.Ready);
                return Task.CompletedTask;
            case State.Finishing:
                return _stopping;
            case State.Recording when _recorder is { } recorder:
                _stopping = FinishAsync(recorder);
                return _stopping;
            default:
                return Task.CompletedTask;
        }
    }

    private async Task FinishAsync(VideoRecorder recorder)
    {
        SetState(State.Finishing);
        _bar?.ShowFinishing();
        _frame?.Close();
        _frame = null;

        var ok = false;
        string? error = null;
        try
        {
            ok = await recorder.StopAsync();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            // Whatever happens, LocalTake must accept new captures afterwards.
            _recorder = null;
            recorder.Dispose();
            CloseUi();
            SetState(State.Idle);
        }

        if (!ok)
        {
            ReleaseNumber();
            TryDelete(recorder.OutputPath);
            var reason = error ?? (recorder.ErrorTail.Length > 0 ? recorder.ErrorTail : "ffmpeg n'a pas terminé le fichier.");
            MessageBox.Show("La vidéo n'a pas pu être finalisée.\n\n" + reason, "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _reservedNumber = null;
        if (_settings.CopyPathAfterSave)
        {
            try
            {
                ImageExporter.CopyText(recorder.OutputPath);
            }
            catch (Exception)
            {
                // The video is saved; a busy clipboard is not worth an error.
            }
        }
        _onSaved(recorder.OutputPath, _fullScreen);
    }

    /// <summary>Abandons the prepared recording, the countdown or the recording; nothing is kept.</summary>
    public void Cancel()
    {
        if (_state is not (State.Ready or State.Countdown or State.Recording))
            return;
        _session++;
        DiscardRecorder();
        CloseUi();
        SetState(State.Idle);
    }

    private void OnUnexpectedExit(VideoRecorder recorder)
    {
        if (_recorder != recorder)
            return;
        var log = recorder.ErrorTail;
        var withAudio = _settings.RecordMicrophone || _settings.RecordSystemAudio;
        DiscardRecorder();
        CloseUi();
        SetState(State.Idle);

        var hint = withAudio
            ? "\n\nSi le message parle d'un périphérique audio, coupez le micro ou le son du PC sur la barre d'enregistrement, " +
              "ou choisissez un autre micro dans les réglages."
            : "";
        MessageBox.Show("L'enregistrement s'est arrêté :\n\n" + (log.Length > 0 ? log : "ffmpeg s'est arrêté sans explication.") + hint,
            "LocalTake", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void DiscardRecorder()
    {
        if (_recorder is not { } recorder)
            return;
        _recorder = null;
        recorder.Kill();
        TryDelete(recorder.OutputPath);
        recorder.Dispose();
        ReleaseNumber();
    }

    /// <summary>
    /// Takes %NUM% when the recording starts, so that an image saved meanwhile gets the next number instead of the
    /// same one.
    /// </summary>
    private void ReserveNumber()
    {
        var before = _settings.Counter;
        ImageExporter.ConsumeCounter(_settings);
        _reservedNumber = _settings.Counter != before ? before : null;
    }

    /// <summary>Gives the number back, unless another file has taken a later one since.</summary>
    private void ReleaseNumber()
    {
        if (_reservedNumber is { } number && _settings.Counter == number + 1)
        {
            _settings.Counter = number;
            _settings.TrySave();
        }
        _reservedNumber = null;
    }

    private void CloseUi()
    {
        _sessionHotkeys.UnregisterAll();
        _frame?.Close();
        _frame = null;
        _bar?.Close();
        _bar = null;
    }

    private void SetState(State state)
    {
        _state = state;
        StateChanged?.Invoke();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An incomplete file left behind is harmless.
        }
    }

    public void Dispose()
    {
        DiscardRecorder();
        CloseUi();
        _sessionHotkeys.Dispose();
    }
}
