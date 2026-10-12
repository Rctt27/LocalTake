using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace LocalTake;

/// <summary>
/// One screen recording at a time: area selection, 3-second countdown, ffmpeg, then the MP4 in the output folder.
/// </summary>
internal sealed class VideoController : IDisposable
{
    private enum State { Idle, Selecting, Countdown, Recording, Finishing }

    private readonly AppSettings _settings;
    private readonly Action<string> _onSaved;

    private State _state = State.Idle;
    private RecordingFrame? _frame;
    private RecordingBar? _bar;
    private VideoRecorder? _recorder;
    private Task _stopping = Task.CompletedTask;

    /// <summary>Incremented on cancel, so that a countdown still awaiting does not start the recording.</summary>
    private int _session;

    /// <summary>The %NUM% value taken by the current video, given back if nothing is kept.</summary>
    private int? _reservedNumber;

    public VideoController(AppSettings settings, Action<string> onSaved)
    {
        _settings = settings;
        _onSaved = onSaved;
    }

    /// <summary>From selection to the end of finalization; image captures wait meanwhile.</summary>
    public bool IsBusy => _state != State.Idle;

    public bool IsRecording => _state is State.Countdown or State.Recording;

    public event Action? StateChanged;

    /// <summary>The same shortcut starts and stops a recording.</summary>
    public async void Toggle(bool fullScreen, bool fromMenu)
    {
        switch (_state)
        {
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
        var target = new Rectangle(chosen.X, chosen.Y, Math.Max(2, chosen.Width & ~1), Math.Max(2, chosen.Height & ~1));
        var session = ++_session;

        if (!fullScreen)
        {
            _frame = new RecordingFrame(target);
            _frame.Show();
        }
        _bar = new RecordingBar();
        _bar.StopRequested += async () => await StopAsync();
        _bar.CancelRequested += Cancel;
        _bar.ShowNear(target);
        SetState(State.Countdown);

        for (var seconds = 3; seconds > 0; seconds--)
        {
            _bar.ShowCountdown(seconds);
            await Task.Delay(1000);
            if (session != _session || _state != State.Countdown)
                return;
        }

        try
        {
            var options = new VideoOptions(Math.Clamp(_settings.VideoFrameRate, 5, 60),
                string.IsNullOrWhiteSpace(_settings.Microphone) ? null : _settings.Microphone,
                _settings.RecordSystemAudio);
            var path = ImageExporter.NextOutputPath(_settings, ".mp4");
            ReserveNumber();
            _recorder = VideoRecorder.Start(ffmpeg, target, path, options,
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

        _bar.ShowRecording();
        SetState(State.Recording);
    }

    /// <summary>
    /// Ends the recording and keeps the video. During the countdown nothing is recorded yet, so this gives up.
    /// While a stop is already in progress, returns that one.
    /// </summary>
    public Task StopAsync()
    {
        switch (_state)
        {
            case State.Countdown:
                Cancel();
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
        _onSaved(recorder.OutputPath);
    }

    /// <summary>Abandons the countdown or the recording; nothing is kept.</summary>
    public void Cancel()
    {
        if (_state is not (State.Countdown or State.Recording))
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
        DiscardRecorder();
        CloseUi();
        SetState(State.Idle);

        var hint = string.IsNullOrWhiteSpace(_settings.Microphone) && !_settings.RecordSystemAudio
            ? ""
            : "\n\nSi le message parle d'un périphérique audio, vérifiez le micro choisi dans les réglages.";
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
    }
}
