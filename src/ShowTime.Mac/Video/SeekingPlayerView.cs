using AppKit;
using AVKit;
using Foundation;
using ShowTime.Core.Transcription;

namespace ShowTime.Mac.Video;

/// <summary>
/// macOS's player view, with ShowTime's keys: ← and → jump 5 seconds (instead of a frame), Home goes back to the
/// start. Space, full screen and the rest stay the player's own. It has the keyboard whenever no subtitle is being
/// typed, so ⌘Z and ⇧⌘Z come here for the subtitles: the window would otherwise take them for its own, empty, undo.
/// </summary>
internal sealed class SeekingPlayerView : AVPlayerView
{
    private const ushort Left = 123, Right = 124, Home = 115;

    private const NSEventModifierMask Modifiers =
        NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ControlKeyMask | NSEventModifierMask.ShiftKeyMask;

    /// <summary>← (-1), → (+1).</summary>
    public event Action<int>? JumpRequested;

    /// <summary>The subtitles that ⌘Z and ⇧⌘Z undo and redo.</summary>
    public SubtitleEditor? Subtitles { get; set; }

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

    [Export("undo:")]
    public void Undo(NSObject? sender) => Subtitles?.Undo();

    [Export("redo:")]
    public void Redo(NSObject? sender) => Subtitles?.Redo();

    [Export("validateMenuItem:")]
    public bool ValidateMenu(NSMenuItem item) => item.Action?.Name switch
    {
        "undo:" => Subtitles?.CanUndo == true,
        "redo:" => Subtitles?.CanRedo == true,
        _ => true
    };

    public override void Layout()
    {
        base.Layout();
        LaidOut?.Invoke();
    }
}
