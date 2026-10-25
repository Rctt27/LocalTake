using AppKit;
using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Platform.MacOS;

namespace LocalTake.Mac.Platform;

/// <summary>
/// The highlight trigger on a Mac: a Carbon shortcut, whose press and release macOS reports without the Accessibility
/// permission. Side mouse buttons are not offered: they cannot be kept from other applications without it.
/// </summary>
internal sealed class MacHighlightInput(IUiThread uiThread) : IHighlightInput, IDisposable
{
    private MacHotkeys? _hotkeys;
    private HotkeyModifiers _modifiers;
    private bool _held;
    private IReadOnlyList<ScreenFrame> _frames = [];
    private IReadOnlyList<PixelRect> _layout = [];

    /// <summary>
    /// The key and every modifier of the shortcut: Option released before Space ends the trail, as on Windows. Read on
    /// the main thread, where Carbon's press and release land too.
    /// </summary>
    public bool IsHeld => _held && ModifiersDown(_modifiers);

    public PixelPoint Cursor
    {
        get
        {
            if (_frames.Count == 0)
                return default;
            var point = NSEvent.CurrentMouseLocation;
            return MacScreenSpace.PointToPixel(_frames, _layout, point.X, point.Y);
        }
    }

    public bool Register(HighlightTrigger trigger, Action pressed)
    {
        Unregister();
        if (trigger.Key is not { } key)
            return false;
        _hotkeys ??= new MacHotkeys(uiThread);
        if (!_hotkeys.Register(key, () =>
            {
                _held = true;
                pressed();
            }, () => _held = false))
            return false;
        _modifiers = key.Modifiers;
        // The screens of this recording, read once rather than on every frame.
        var screens = ScreenInfo.Current();
        _frames = [.. screens.Select(s => s.Frame)];
        _layout = [.. screens.Select(s => s.Pixels)];
        return true;
    }

    public void Unregister()
    {
        _hotkeys?.UnregisterAll();
        _held = false;
    }

    public void Dispose()
    {
        Unregister();
        _hotkeys?.Dispose();
        _hotkeys = null;
    }

    private static bool ModifiersDown(HotkeyModifiers modifiers)
    {
        var flags = NSEvent.CurrentModifierFlags;
        return (!modifiers.HasFlag(HotkeyModifiers.Control) || flags.HasFlag(NSEventModifierMask.ControlKeyMask)) &&
               (!modifiers.HasFlag(HotkeyModifiers.Alt) || flags.HasFlag(NSEventModifierMask.AlternateKeyMask)) &&
               (!modifiers.HasFlag(HotkeyModifiers.Shift) || flags.HasFlag(NSEventModifierMask.ShiftKeyMask)) &&
               (!modifiers.HasFlag(HotkeyModifiers.Meta) || flags.HasFlag(NSEventModifierMask.CommandKeyMask));
    }
}
