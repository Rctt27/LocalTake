using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LocalTake;

/// <summary>A global shortcut written as "Ctrl+Alt+PrintScreen".</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= ModifierKeys.Control; break;
                case "ALT": modifiers |= ModifierKeys.Alt; break;
                case "SHIFT": modifiers |= ModifierKeys.Shift; break;
                case "WIN" or "WINDOWS": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        var key = ParseKey(parts[^1]);
        if (key == Key.None || (modifiers == ModifierKeys.None && !AllowedWithoutModifier(key)))
            return false;
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    /// <summary>
    /// A plain letter, Enter or Space registered system-wide would stop working in every other application.
    /// </summary>
    public static bool AllowedWithoutModifier(Key key) => key is Key.Snapshot or (>= Key.F1 and <= Key.F24);

    private static Key ParseKey(string name)
    {
        switch (name.ToUpperInvariant())
        {
            case "PRINTSCREEN" or "PRTSCRN" or "PRTSC" or "SNAPSHOT": return Key.Snapshot;
        }
        if (name.Length == 1 && char.IsDigit(name[0]))
            return Key.D0 + (name[0] - '0');
        return Enum.TryParse<Key>(name, ignoreCase: true, out var key) ? key : Key.None;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Control) ? Native.MOD_CONTROL : 0) |
        (Modifiers.HasFlag(ModifierKeys.Alt) ? Native.MOD_ALT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? Native.MOD_SHIFT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? Native.MOD_WIN : 0);

    public override string ToString()
    {
        var text = "";
        if (Modifiers.HasFlag(ModifierKeys.Control)) text += "Ctrl+";
        if (Modifiers.HasFlag(ModifierKeys.Alt)) text += "Alt+";
        if (Modifiers.HasFlag(ModifierKeys.Shift)) text += "Shift+";
        if (Modifiers.HasFlag(ModifierKeys.Windows)) text += "Win+";
        return text + Key switch
        {
            Key.Snapshot => "PrintScreen",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (Key - Key.D0))).ToString(),
            _ => Key.ToString()
        };
    }
}

/// <summary>Registers system-wide shortcuts on a message-only window.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("LocalTakeHotkeys") { ParentWindow = HwndMessage });
        _source.AddHook(WndProc);
    }

    public bool Register(Hotkey hotkey, Action action)
    {
        var id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, hotkey.NativeModifiers | Native.MOD_NOREPEAT, hotkey.VirtualKey))
            return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            Native.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            // Captures open modal windows: run them outside this window procedure.
            Dispatcher.CurrentDispatcher.BeginInvoke(action);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
