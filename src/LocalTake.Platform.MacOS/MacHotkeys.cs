using System.Runtime.InteropServices;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;

namespace LocalTake.Platform.MacOS;

/// <summary>macOS virtual key codes (kVK_*) and Carbon modifier masks.</summary>
public static class MacKeys
{
    private static readonly Dictionary<KeyCode, uint> Codes = new()
    {
        [KeyCode.A] = 0x00,
        [KeyCode.S] = 0x01,
        [KeyCode.D] = 0x02,
        [KeyCode.F] = 0x03,
        [KeyCode.H] = 0x04,
        [KeyCode.G] = 0x05,
        [KeyCode.Z] = 0x06,
        [KeyCode.X] = 0x07,
        [KeyCode.C] = 0x08,
        [KeyCode.V] = 0x09,
        [KeyCode.B] = 0x0B,
        [KeyCode.Q] = 0x0C,
        [KeyCode.W] = 0x0D,
        [KeyCode.E] = 0x0E,
        [KeyCode.R] = 0x0F,
        [KeyCode.Y] = 0x10,
        [KeyCode.T] = 0x11,
        [KeyCode.D1] = 0x12,
        [KeyCode.D2] = 0x13,
        [KeyCode.D3] = 0x14,
        [KeyCode.D4] = 0x15,
        [KeyCode.D6] = 0x16,
        [KeyCode.D5] = 0x17,
        [KeyCode.OemPlus] = 0x18,
        [KeyCode.D9] = 0x19,
        [KeyCode.D7] = 0x1A,
        [KeyCode.OemMinus] = 0x1B,
        [KeyCode.D8] = 0x1C,
        [KeyCode.D0] = 0x1D,
        [KeyCode.O] = 0x1F,
        [KeyCode.U] = 0x20,
        [KeyCode.I] = 0x22,
        [KeyCode.P] = 0x23,
        [KeyCode.Enter] = 0x24,
        [KeyCode.L] = 0x25,
        [KeyCode.J] = 0x26,
        [KeyCode.K] = 0x28,
        [KeyCode.OemComma] = 0x2B,
        [KeyCode.N] = 0x2D,
        [KeyCode.M] = 0x2E,
        [KeyCode.OemPeriod] = 0x2F,
        [KeyCode.Tab] = 0x30,
        [KeyCode.Space] = 0x31,
        [KeyCode.Backspace] = 0x33,
        [KeyCode.Escape] = 0x35,
        [KeyCode.Decimal] = 0x41,
        [KeyCode.Multiply] = 0x43,
        [KeyCode.Add] = 0x45,
        [KeyCode.Divide] = 0x4B,
        [KeyCode.Subtract] = 0x4E,
        [KeyCode.NumPad0] = 0x52,
        [KeyCode.NumPad1] = 0x53,
        [KeyCode.NumPad2] = 0x54,
        [KeyCode.NumPad3] = 0x55,
        [KeyCode.NumPad4] = 0x56,
        [KeyCode.NumPad5] = 0x57,
        [KeyCode.NumPad6] = 0x58,
        [KeyCode.NumPad7] = 0x59,
        [KeyCode.NumPad8] = 0x5B,
        [KeyCode.NumPad9] = 0x5C,
        [KeyCode.F1] = 0x7A,
        [KeyCode.F2] = 0x78,
        [KeyCode.F3] = 0x63,
        [KeyCode.F4] = 0x76,
        [KeyCode.F5] = 0x60,
        [KeyCode.F6] = 0x61,
        [KeyCode.F7] = 0x62,
        [KeyCode.F8] = 0x64,
        [KeyCode.F9] = 0x65,
        [KeyCode.F10] = 0x6D,
        [KeyCode.F11] = 0x67,
        [KeyCode.F12] = 0x6F,
        [KeyCode.F13] = 0x69,
        [KeyCode.F14] = 0x6B,
        [KeyCode.F15] = 0x71,
        [KeyCode.F16] = 0x6A,
        [KeyCode.F17] = 0x40,
        [KeyCode.F18] = 0x4F,
        [KeyCode.F19] = 0x50,
        [KeyCode.F20] = 0x5A,
        [KeyCode.Insert] = 0x72,
        [KeyCode.Home] = 0x73,
        [KeyCode.PageUp] = 0x74,
        [KeyCode.Delete] = 0x75,
        [KeyCode.End] = 0x77,
        [KeyCode.PageDown] = 0x79,
        [KeyCode.Left] = 0x7B,
        [KeyCode.Right] = 0x7C,
        [KeyCode.Down] = 0x7D,
        [KeyCode.Up] = 0x7E
    };

    /// <summary>A Mac keyboard has no Print Screen, Pause, Scroll Lock or F21 to F24.</summary>
    public static bool TryGetKeyCode(KeyCode key, out uint code) => Codes.TryGetValue(key, out code);

    private static readonly Dictionary<uint, KeyCode> Keys = Codes.ToDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>The key of a virtual key code (NSEvent.KeyCode), or None for a key LocalTake does not know.</summary>
    public static KeyCode FromKeyCode(uint code) => Keys.GetValueOrDefault(code, KeyCode.None);

    /// <summary>
    /// The character an NSMenuItem shows for the key, as its key equivalent (the function keys use AppKit's private-use
    /// characters, NSF1FunctionKey and the like). Null when a menu cannot show the key.
    /// </summary>
    public static string? KeyEquivalent(KeyCode key) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => ((char)('a' + (key - KeyCode.A))).ToString(),
        >= KeyCode.D0 and <= KeyCode.D9 => ((char)('0' + (key - KeyCode.D0))).ToString(),
        >= KeyCode.NumPad0 and <= KeyCode.NumPad9 => ((char)('0' + (key - KeyCode.NumPad0))).ToString(),
        >= KeyCode.F1 and <= KeyCode.F20 => ((char)(0xF704 + (key - KeyCode.F1))).ToString(),
        KeyCode.Up => "",
        KeyCode.Down => "",
        KeyCode.Left => "",
        KeyCode.Right => "",
        KeyCode.Delete => "",
        KeyCode.Home => "",
        KeyCode.End => "",
        KeyCode.PageUp => "",
        KeyCode.PageDown => "",
        KeyCode.Space => " ",
        KeyCode.Enter => "\r",
        KeyCode.Tab => "\t",
        KeyCode.Escape => "\u001B",
        KeyCode.Backspace => "\b",
        KeyCode.Multiply => "*",
        KeyCode.Add => "+",
        KeyCode.Subtract or KeyCode.OemMinus => "-",
        KeyCode.Decimal or KeyCode.OemPeriod => ".",
        KeyCode.Divide => "/",
        KeyCode.OemPlus => "=",
        KeyCode.OemComma => ",",
        _ => null
    };

    /// <summary>
    /// Whether macOS uses this key and these Carbon modifiers for one of its own shortcuts, enabled in System Settings
    /// > Keyboard > Keyboard Shortcuts (Cmd+Shift+3 for a screenshot, for instance). Registering it would succeed but
    /// never fire.
    /// </summary>
    public static unsafe bool IsSystemShortcut(uint code, uint modifiers)
    {
        if (Carbon.CopySymbolicHotKeys(out var list) != 0 || list == 0)
            return false;
        var codeKey = Quartz.CFString("kHISymbolicHotKeyCode");
        var modifiersKey = Quartz.CFString("kHISymbolicHotKeyModifiers");
        var enabledKey = Quartz.CFString("kHISymbolicHotKeyEnabled");
        try
        {
            var count = Quartz.CFArrayGetCount(list);
            for (nint i = 0; i < count; i++)
            {
                var entry = Quartz.CFArrayGetValueAtIndex(list, i);
                int entryCode = -1, entryModifiers = -1;
                var enabled = Quartz.CFDictionaryGetValue(entry, enabledKey);
                var codeValue = Quartz.CFDictionaryGetValue(entry, codeKey);
                var modifiersValue = Quartz.CFDictionaryGetValue(entry, modifiersKey);
                if (enabled == 0 || !Quartz.CFBooleanGetValue(enabled) || codeValue == 0 || modifiersValue == 0)
                    continue;
                Quartz.CFNumberGetValue(codeValue, Quartz.kCFNumberSInt32Type, &entryCode);
                Quartz.CFNumberGetValue(modifiersValue, Quartz.kCFNumberSInt32Type, &entryModifiers);
                if (entryCode == code && entryModifiers == modifiers)
                    return true;
            }
            return false;
        }
        finally
        {
            Quartz.CFRelease(codeKey);
            Quartz.CFRelease(modifiersKey);
            Quartz.CFRelease(enabledKey);
            Quartz.CFRelease(list);
        }
    }

    public static uint Modifiers(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Control) ? Carbon.controlKey : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Alt) ? Carbon.optionKey : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Shift) ? Carbon.shiftKey : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Meta) ? Carbon.cmdKey : 0);
}

/// <summary>
/// System-wide shortcuts through Carbon's RegisterEventHotKey, which, unlike event taps, needs no Accessibility
/// permission. Carbon delivers them on the main thread through the application's run loop, released as well as
/// pressed, which the highlight trigger needs to know how long it is held.
/// </summary>
public sealed unsafe class MacHotkeys : IGlobalHotkeys
{
    private const uint Signature = 0x4C54616B; // 'LTak'
    private static readonly Dictionary<uint, (Action Pressed, Action Released)> Actions = [];
    private static readonly Lock Gate = new();
    private static bool _handlerInstalled;
    private static uint _nextId = 1;

    private readonly IUiThread _uiThread;
    private readonly Dictionary<uint, nint> _registered = [];

    public MacHotkeys(IUiThread uiThread)
    {
        _uiThread = uiThread;
        InstallHandler();
    }

    public bool Register(Hotkey hotkey, Action action) => Register(hotkey, action, static () => { });

    /// <param name="released">Runs on the UI thread when the shortcut's key is released.</param>
    public bool Register(Hotkey hotkey, Action pressed, Action released)
    {
        if (!MacKeys.TryGetKeyCode(hotkey.Key, out var code))
            return false;
        var modifiers = MacKeys.Modifiers(hotkey.Modifiers);
        // Carbon accepts a shortcut of macOS itself, which then never reaches LocalTake.
        if (MacKeys.IsSystemShortcut(code, modifiers))
            return false;
        uint id;
        lock (Gate)
            id = _nextId++;
        // Exclusive: refused when another application holds the combination, and kept from other applications after.
        var status = Carbon.RegisterEventHotKey(code, modifiers,
            new Carbon.EventHotKeyID { Signature = Signature, Id = id }, Carbon.GetApplicationEventTarget(),
            Carbon.kEventHotKeyExclusive, out var reference);
        if (status != 0)
            return false;
        lock (Gate)
            Actions[id] = (() => _uiThread.Post(pressed), () => _uiThread.Post(released));
        _registered[id] = reference;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var (id, reference) in _registered)
        {
            _ = Carbon.UnregisterEventHotKey(reference);
            lock (Gate)
                Actions.Remove(id);
        }
        _registered.Clear();
    }

    public void Dispose() => UnregisterAll();

    private static void InstallHandler()
    {
        lock (Gate)
        {
            if (_handlerInstalled)
                return;
            var specs = stackalloc Carbon.EventTypeSpec[]
            {
                new() { EventClass = Carbon.kEventClassKeyboard, EventKind = Carbon.kEventHotKeyPressed },
                new() { EventClass = Carbon.kEventClassKeyboard, EventKind = Carbon.kEventHotKeyReleased }
            };
            var status = Carbon.InstallEventHandler(Carbon.GetApplicationEventTarget(), &OnHotKey, 2, specs, 0, out _);
            if (status != 0)
                throw new InvalidOperationException("Carbon refused the shortcut handler: " + status);
            _handlerInstalled = true;
        }
    }

    [UnmanagedCallersOnly]
    private static int OnHotKey(nint handlerCall, nint evt, nint userData)
    {
        Carbon.EventHotKeyID id;
        if (Carbon.GetEventParameter(evt, Carbon.kEventParamDirectObject, Carbon.typeEventHotKeyID, 0,
                (nuint)sizeof(Carbon.EventHotKeyID), 0, &id) != 0 || id.Signature != Signature)
            return -9874; // eventNotHandledErr
        (Action Pressed, Action Released) actions;
        bool found;
        lock (Gate)
            found = Actions.TryGetValue(id.Id, out actions);
        if (found)
            (Carbon.GetEventKind(evt) == Carbon.kEventHotKeyReleased ? actions.Released : actions.Pressed)();
        return 0;
    }
}
