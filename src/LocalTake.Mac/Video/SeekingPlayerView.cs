using AppKit;
using AVKit;

namespace LocalTake.Mac.Video;

/// <summary>
/// macOS's player view, with LocalTake's keys: ← and → jump 5 seconds (instead of a frame), Home goes back to the
/// start. Space, full screen and the rest stay the player's own.
/// </summary>
internal sealed class SeekingPlayerView : AVPlayerView
{
    private const ushort Left = 123, Right = 124, Home = 115;

    private const NSEventModifierMask Modifiers =
        NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ControlKeyMask | NSEventModifierMask.ShiftKeyMask;

    /// <summary>← (-1), → (+1).</summary>
    public event Action<int>? JumpRequested;

    public event Action? HomeRequested;

    /// <summary>The view was laid out: the subtitle band follows the picture.</summary>
    public event Action? LaidOut;

    public override void KeyDown(NSEvent theEvent)
    {
        // The arrows carry the function and numeric pad flags: only the real modifiers count.
        if ((theEvent.ModifierFlags & Modifiers) == 0)
        {
            switch (theEvent.KeyCode)
            {
                case Left:
                    JumpRequested?.Invoke(-1);
                    return;
                case Right:
                    JumpRequested?.Invoke(1);
                    return;
                case Home:
                    HomeRequested?.Invoke();
                    return;
            }
        }
        base.KeyDown(theEvent);
    }

    public override void Layout()
    {
        base.Layout();
        LaidOut?.Invoke();
    }
}
