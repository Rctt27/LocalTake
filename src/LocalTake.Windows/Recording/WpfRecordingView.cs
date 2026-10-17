using System;
using System.Collections.Generic;
using LocalTake.Adapters;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;

namespace LocalTake;

/// <summary>The recording bar and, for an area, the red frame around it.</summary>
internal sealed class WpfRecordingView : IRecordingView
{
    private readonly RecordingBar _bar;
    private RecordingFrame? _frame;

    public WpfRecordingView(AppSettings settings, ISettingsStore store, IFfmpegPlatform platform, INativeWindows windows, PixelRect area, bool fullScreen)
    {
        if (!fullScreen)
        {
            _frame = new RecordingFrame(area.ToRectangle(), windows);
            _frame.Show();
        }
        _bar = new RecordingBar(settings, store, platform, windows);
        _bar.StartRequested += () => StartRequested?.Invoke(this, EventArgs.Empty);
        _bar.StopRequested += () => StopRequested?.Invoke(this, EventArgs.Empty);
        _bar.CancelRequested += () => CancelRequested?.Invoke(this, EventArgs.Empty);
        _bar.ShowNear(area);
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
        CloseFrame();
    }

    public void Dispose()
    {
        CloseFrame();
        _bar.Close();
    }

    private void CloseFrame()
    {
        _frame?.Close();
        _frame?.Dispose();
        _frame = null;
    }
}

internal sealed class WpfRecordingViewFactory(AppSettings settings, ISettingsStore store, IFfmpegPlatform platform, INativeWindows windows) : IRecordingViewFactory
{
    public IRecordingView Show(PixelRect area, bool fullScreen) => new WpfRecordingView(settings, store, platform, windows, area, fullScreen);
}
