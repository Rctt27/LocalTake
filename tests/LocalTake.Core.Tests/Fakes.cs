using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using Microsoft.Extensions.Time.Testing;

namespace LocalTake.Core.Tests;

internal sealed class FakeImage(int width = 10, int height = 10) : IImage
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

/// <summary>Runs posted actions at once, as if every caller were on the UI thread.</summary>
internal sealed class ImmediateUiThread : IUiThread
{
    public void Post(Action action) => action();
}

/// <summary>A settings store that keeps nothing.</summary>
internal sealed class MemorySettingsStore : ISettingsStore
{
    public int Saves { get; private set; }
    public AppSettings Load() => new(SettingsDefaults.ForWindows("C:\\Out"));
    public void Save(AppSettings settings) => Saves++;
    public bool TrySave(AppSettings settings)
    {
        Saves++;
        return true;
    }
}

internal sealed class FakeHotkeys : IGlobalHotkeys
{
    private readonly Dictionary<Hotkey, Action> _registered = [];

    public HashSet<Hotkey> Refused { get; } = [];
    public IReadOnlyCollection<Hotkey> Registered => _registered.Keys;

    public bool Register(Hotkey hotkey, Action action)
    {
        if (Refused.Contains(hotkey))
            return false;
        _registered[hotkey] = action;
        return true;
    }

    public void Press(string text)
    {
        Hotkey.TryParse(text, out var hotkey).ShouldBeTrue();
        _registered[hotkey]();
    }

    public void UnregisterAll() => _registered.Clear();
    public void Dispose() => UnregisterAll();
}

internal sealed class FakeRecordingView : IRecordingView
{
    public List<string> Calls { get; } = [];
    public IReadOnlyCollection<string>? Devices { get; private set; }
    public bool Closed { get; private set; }

    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? CancelRequested;

    public void ClickStart() => StartRequested?.Invoke(this, EventArgs.Empty);
    public void ClickStop() => StopRequested?.Invoke(this, EventArgs.Empty);
    public void ClickCancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    public void ShowReady(string? startShortcut, string? stopShortcut) => Calls.Add($"ready {startShortcut} {stopShortcut}");
    public void SetAudioDevices(IReadOnlyCollection<string> devices) => Devices = devices;
    public void ShowCountdown(int seconds) => Calls.Add($"countdown {seconds}");
    public void ShowStarting() => Calls.Add("starting");
    public void ShowRecording() => Calls.Add("recording");
    public void ShowFinishing() => Calls.Add("finishing");
    public void Dispose() => Closed = true;
}

internal sealed class FakeRecordingViewFactory : IRecordingViewFactory
{
    public FakeRecordingView? View { get; private set; }
    public PixelRect? Area { get; private set; }
    public bool? FullScreen { get; private set; }

    public IRecordingView Show(PixelRect area, bool fullScreen)
    {
        Area = area;
        FullScreen = fullScreen;
        return View = new FakeRecordingView();
    }
}

internal sealed class FakeRecorder(string outputPath) : IVideoRecorder
{
    public TaskCompletionSource<bool> CapturingSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> StopResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Action<IVideoRecorder>? OnUnexpectedExit { get; set; }

    public string OutputPath { get; } = outputPath;
    public string ErrorTail { get; set; } = "";
    public Task<bool> Capturing => CapturingSource.Task;
    public bool StopRequested { get; private set; }
    public bool Killed { get; private set; }
    public bool Disposed { get; private set; }

    public Task<bool> StopAsync()
    {
        StopRequested = true;
        return StopResult.Task;
    }

    public void Kill() => Killed = true;
    public void Dispose() => Disposed = true;
}

internal sealed class FakeRecorderFactory : IVideoRecorderFactory
{
    public FakeRecorder? Recorder { get; private set; }
    public VideoOptions? Options { get; private set; }
    public PixelRect? Area { get; private set; }
    public Exception? StartFailure { get; set; }

    /// <summary>The clock whose time is noted at each launch, in <see cref="StartedAt"/>.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Forgets the last recorder, so that a test can wait for the next one.</summary>
    public void Reset() => Recorder = null;

    public IVideoRecorder Start(string ffmpegPath, PixelRect area, string outputPath, VideoOptions options, Action<IVideoRecorder> onUnexpectedExit)
    {
        if (StartFailure is not null)
            throw StartFailure;
        Area = area;
        Options = options;
        StartedAt = Clock.GetUtcNow();
        return Recorder = new FakeRecorder(outputPath) { OnUnexpectedExit = onUnexpectedExit };
    }
}

internal static class FakeTime
{
    /// <summary>Advances the clock in small steps until the task completes, as the real one would.</summary>
    public static async Task RunUntil(this FakeTimeProvider clock, Task task, int maxSeconds = 60)
    {
        for (var elapsed = 0; !task.IsCompleted && elapsed < maxSeconds * 10; elapsed++)
        {
            await Task.Delay(1);
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        await task;
    }
}
