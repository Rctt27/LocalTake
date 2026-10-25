using AppKit;
using LocalTake.Core.Hotkeys;
using LocalTake.Platform.MacOS;

namespace LocalTake.Mac.Settings;

/// <summary>
/// A box that records the shortcut pressed while it has the focus, and shows it with Mac names (Ctrl, Option, Maj,
/// Cmd). Delete or Backspace clears it; Tab, Return and Escape keep their usual role.
/// </summary>
internal sealed class HotkeyField : NSTextField
{
    private const ushort Tab = 48, Return = 36, Escape = 53, Backspace = 51, ForwardDelete = 117;

    public HotkeyField(string hotkey)
    {
        Editable = false;
        Selectable = false;
        Bezeled = true;
        DrawsBackground = true;
        BackgroundColor = NSColor.TextBackground;
        Shortcut = hotkey;
        PlaceholderString = "Aucun";
        ToolTip = "Cliquez puis appuyez sur la combinaison. ⌫ : aucun raccourci.";
    }

    /// <summary>
    /// Whether a key may be used alone (Space, a letter): for the highlight, only registered while recording.
    /// </summary>
    public bool AllowPlainKey { get; init; }

    /// <summary>As settings.json stores it; an older file's Windows names (Alt, Win) are shown with Mac names.</summary>
    public string Shortcut
    {
        get => StringValue;
        set => StringValue = Hotkey.TryParse(value, out var parsed, AllowPlainKey) ? parsed.Format(HotkeyNaming.MacOS) : value;
    }

    /// <summary>Raised when the shortcut changes by a key press.</summary>
    public event EventHandler? ShortcutChanged;

    public override bool AcceptsFirstResponder() => true;

    public override bool CanBecomeKeyView => true;

    public override bool BecomeFirstResponder()
    {
        BackgroundColor = NSColor.SelectedTextBackground;
        return base.BecomeFirstResponder();
    }

    public override bool ResignFirstResponder()
    {
        BackgroundColor = NSColor.TextBackground;
        return base.ResignFirstResponder();
    }

    public override void MouseDown(NSEvent theEvent) => Window?.MakeFirstResponder(this);

    /// <summary>Cmd combinations come here first: taken as the shortcut rather than as a menu command.</summary>
    public override bool PerformKeyEquivalent(NSEvent theEvent)
    {
        if (Window?.FirstResponder != this)
            return base.PerformKeyEquivalent(theEvent);
        KeyDown(theEvent);
        return true;
    }

    public override void KeyDown(NSEvent theEvent)
    {
        var modifiers = Modifiers(theEvent.ModifierFlags);
        var code = theEvent.KeyCode;
        if (modifiers == HotkeyModifiers.None)
        {
            switch (code)
            {
                case Tab:
                    Window?.SelectNextKeyView(this);
                    return;
                case Return or Escape:
                    // The window's Enregistrer and Annuler buttons.
                    Window?.PerformKeyEquivalent(theEvent);
                    return;
                case Backspace or ForwardDelete:
                    StringValue = "";
                    ShortcutChanged?.Invoke(this, EventArgs.Empty);
                    return;
            }
        }
        if (new Hotkey(modifiers, MacKeys.FromKeyCode(code)) is var hotkey && hotkey.Key != KeyCode.None && (hotkey.IsValid || AllowPlainKey))
        {
            StringValue = hotkey.Format(HotkeyNaming.MacOS);
            ShortcutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static HotkeyModifiers Modifiers(NSEventModifierMask flags) =>
        (flags.HasFlag(NSEventModifierMask.ControlKeyMask) ? HotkeyModifiers.Control : 0) |
        (flags.HasFlag(NSEventModifierMask.AlternateKeyMask) ? HotkeyModifiers.Alt : 0) |
        (flags.HasFlag(NSEventModifierMask.ShiftKeyMask) ? HotkeyModifiers.Shift : 0) |
        (flags.HasFlag(NSEventModifierMask.CommandKeyMask) ? HotkeyModifiers.Meta : 0);
}
