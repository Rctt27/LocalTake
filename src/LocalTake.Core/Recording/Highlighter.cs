using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;

namespace LocalTake.Core.Recording;

/// <summary>
/// While a recording is under way, holding the trigger draws a translucent trail behind the mouse, which the video
/// captures; releasing it fades the trail out. Every member is called on the UI thread.
/// </summary>
public sealed class Highlighter(
    AppSettings settings, IHighlightInput input, IHighlightOverlayFactory overlays, IUiThread uiThread,
    TimeProvider clock, HotkeyNaming hotkeyNaming) : IDisposable
{
    /// <summary>About 60 frames per second, above any recording frame rate.</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly HighlightTrail _trail = new();
    private IHighlightOverlay? _overlay;
    private ITimer? _frames;
    private bool _registered;
    private bool _held;
    private long _releasedAt;

    /// <summary>Registers the trigger for a recording of <paramref name="area"/>.</summary>
    /// <returns>The trigger as the bar shows it, or null when it is empty, invalid or refused.</returns>
    public string? Enable(PixelRect area)
    {
        Disable();
        if (!HighlightTrigger.TryParse(settings.HighlightTrigger, out var trigger) || !input.Register(trigger, OnPressed))
            return null;
        _registered = true;
        _overlay = overlays.Create(area);
        return trigger.Format(hotkeyNaming);
    }

    public void Disable()
    {
        if (_registered)
            input.Unregister();
        _registered = false;
        StopFrames();
        _overlay?.Dispose();
        _overlay = null;
        _held = false;
    }

    public void Dispose() => Disable();

    private void OnPressed()
    {
        // Carbon may repeat the press while the key is held.
        if (_overlay is not { } overlay || _held)
            return;
        _held = true;
        _trail.Begin(input.Cursor);
        overlay.SetOpacity(HighlightTrail.FullOpacity);
        overlay.Draw(_trail.Points);
        _frames ??= clock.CreateTimer(_ => uiThread.Post(OnFrame), null, FrameInterval, FrameInterval);
    }

    private void OnFrame()
    {
        if (_overlay is not { } overlay)
            return;
        if (_held)
        {
            if (!input.IsHeld)
            {
                _held = false;
                _releasedAt = clock.GetTimestamp();
            }
            else if (_trail.Add(input.Cursor))
            {
                overlay.Draw(_trail.Points);
            }
            return;
        }

        var opacity = HighlightTrail.FadeOpacity(clock.GetElapsedTime(_releasedAt));
        if (opacity > 0)
        {
            overlay.SetOpacity(opacity);
            return;
        }
        overlay.Hide();
        StopFrames();
    }

    private void StopFrames()
    {
        _frames?.Dispose();
        _frames = null;
    }
}
