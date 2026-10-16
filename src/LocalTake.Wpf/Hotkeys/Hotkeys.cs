using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;

namespace LocalTake;

/// <summary>WPF keys and the core's framework-neutral key codes.</summary>
internal static class WpfKeyMap
{
    private static readonly Dictionary<KeyCode, Key> ToWpf = Enum.GetValues<KeyCode>()
        .Where(code => code != KeyCode.None)
        .ToDictionary(code => code, code => code switch
        {
            KeyCode.PrintScreen => Key.Snapshot,
            KeyCode.Backspace => Key.Back,
            KeyCode.ScrollLock => Key.Scroll,
            _ => Enum.Parse<Key>(code.ToString())
        });

    private static readonly Dictionary<Key, KeyCode> ToCore = ToWpf.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static KeyCode ToKeyCode(Key key) => ToCore.GetValueOrDefault(key, KeyCode.None);

    public static uint VirtualKey(KeyCode code) => (uint)KeyInterop.VirtualKeyFromKey(ToWpf[code]);

    public static HotkeyModifiers ToModifiers(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Control : 0) |
        (modifiers.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0) |
        (modifiers.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0) |
        (modifiers.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Meta : 0);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public static uint NativeModifiers(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Control) ? Native.MOD_CONTROL : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Alt) ? Native.MOD_ALT : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Shift) ? Native.MOD_SHIFT : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Meta) ? Native.MOD_WIN : 0);
}

/// <summary>Registers system-wide shortcuts on a message-only window.</summary>
internal sealed class HotkeyManager : IGlobalHotkeys
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
        var modifiers = WpfKeyMap.NativeModifiers(hotkey.Modifiers) | Native.MOD_NOREPEAT;
        if (!Native.RegisterHotKey(_source.Handle, id, modifiers, WpfKeyMap.VirtualKey(hotkey.Key)))
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
