using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ShowTime.Core.Hotkeys;

namespace ShowTime;

/// <summary>WPF keys and the core's framework-neutral key codes, for typing a shortcut in the settings.</summary>
internal static class WpfKeyMap
{
    private static readonly Dictionary<Key, KeyCode> ToCore = Enum.GetValues<KeyCode>()
        .Where(code => code != KeyCode.None)
        .ToDictionary(code => code switch
        {
            KeyCode.PrintScreen => Key.Snapshot,
            KeyCode.Backspace => Key.Back,
            KeyCode.ScrollLock => Key.Scroll,
            _ => Enum.Parse<Key>(code.ToString())
        }, code => code);

    public static KeyCode ToKeyCode(Key key) => ToCore.GetValueOrDefault(key, KeyCode.None);

    public static HotkeyModifiers ToModifiers(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Control : 0) |
        (modifiers.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0) |
        (modifiers.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0) |
        (modifiers.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Meta : 0);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
