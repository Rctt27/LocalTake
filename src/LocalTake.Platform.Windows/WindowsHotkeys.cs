
using System.Runtime.InteropServices;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;

namespace LocalTake.Platform.Windows;

/// <summary>Windows virtual-key codes and RegisterHotKey modifier flags.</summary>
public static class WindowsKeys
{
    private static readonly Dictionary<KeyCode, uint> VirtualKeys = Build();

    public static uint VirtualKey(KeyCode key) => VirtualKeys[key];

    public static uint Modifiers(HotkeyModifiers modifiers) =>
        (modifiers.HasFlag(HotkeyModifiers.Control) ? Win32.MOD_CONTROL : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Alt) ? Win32.MOD_ALT : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Shift) ? Win32.MOD_SHIFT : 0) |
        (modifiers.HasFlag(HotkeyModifiers.Meta) ? Win32.MOD_WIN : 0);

    private static Dictionary<KeyCode, uint> Build()
    {
        var keys = new Dictionary<KeyCode, uint>
        {
            [KeyCode.Multiply] = 0x6A,
            [KeyCode.Add] = 0x6B,
            [KeyCode.Subtract] = 0x6D,
            [KeyCode.Decimal] = 0x6E,
            [KeyCode.Divide] = 0x6F,
            [KeyCode.PrintScreen] = 0x2C,
            [KeyCode.Pause] = 0x13,
            [KeyCode.ScrollLock] = 0x91,
            [KeyCode.Insert] = 0x2D,
            [KeyCode.Delete] = 0x2E,
            [KeyCode.Home] = 0x24,
            [KeyCode.End] = 0x23,
            [KeyCode.PageUp] = 0x21,
            [KeyCode.PageDown] = 0x22,
            [KeyCode.Left] = 0x25,
            [KeyCode.Up] = 0x26,
            [KeyCode.Right] = 0x27,
            [KeyCode.Down] = 0x28,
            [KeyCode.Space] = 0x20,
            [KeyCode.Enter] = 0x0D,
            [KeyCode.Tab] = 0x09,
            [KeyCode.Escape] = 0x1B,
            [KeyCode.Backspace] = 0x08,
            [KeyCode.OemPlus] = 0xBB,
            [KeyCode.OemMinus] = 0xBD,
            [KeyCode.OemComma] = 0xBC,
            [KeyCode.OemPeriod] = 0xBE
        };
        for (var i = 0; i < 26; i++)
            keys[KeyCode.A + i] = (uint)('A' + i);
        for (var i = 0; i < 10; i++)
        {
            keys[KeyCode.D0 + i] = (uint)('0' + i);
            keys[KeyCode.NumPad0 + i] = 0x60u + (uint)i;
        }
        for (var i = 0; i < 24; i++)
            keys[KeyCode.F1 + i] = 0x70u + (uint)i;
        return keys;
    }
}

/// <summary>
/// System-wide shortcuts registered on a message-only window. Avalonia's message loop delivers WM_HOTKEY to it, since
/// it is created on the UI thread.
/// </summary>
public sealed unsafe class WindowsHotkeys : IGlobalHotkeys
{
    private const string ClassName = "LocalTakeHotkeys";
    private static readonly Dictionary<nint, WindowsHotkeys> Instances = [];
    private static bool _classRegistered;

    private readonly IUiThread _uiThread;
    private readonly Dictionary<int, Action> _actions = [];
    private readonly nint _window;
    private int _nextId = 1;

    public WindowsHotkeys(IUiThread uiThread)
    {
        _uiThread = uiThread;
        RegisterClass();
        _window = Win32.CreateWindowExW(0, ClassName, ClassName, 0, 0, 0, 0, 0, Win32.HWND_MESSAGE, 0, Win32.GetModuleHandleW(null), 0);
        if (_window == 0)
            throw new InvalidOperationException("Cannot create the shortcut window: error " + Marshal.GetLastPInvokeError());
        lock (Instances)
            Instances[_window] = this;
    }

    public bool Register(Hotkey hotkey, Action action)
    {
        var id = _nextId++;
        if (!Win32.RegisterHotKey(_window, id, WindowsKeys.Modifiers(hotkey.Modifiers) | Win32.MOD_NOREPEAT, WindowsKeys.VirtualKey(hotkey.Key)))
            return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            Win32.UnregisterHotKey(_window, id);
        _actions.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        lock (Instances)
            Instances.Remove(_window);
        Win32.DestroyWindow(_window);
    }

    private static void RegisterClass()
    {
        lock (Instances)
        {
            if (_classRegistered)
                return;
            fixed (char* name = ClassName)
            {
                var windowClass = new Win32.WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(Win32.WNDCLASSEXW),
                    lpfnWndProc = &WindowProc,
                    hInstance = Win32.GetModuleHandleW(null),
                    lpszClassName = name
                };
                if (Win32.RegisterClassExW(&windowClass) == 0)
                    throw new InvalidOperationException("Cannot register the shortcut window class: error " + Marshal.GetLastPInvokeError());
            }
            _classRegistered = true;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == Win32.WM_HOTKEY)
        {
            WindowsHotkeys? owner;
            lock (Instances)
                Instances.TryGetValue(hwnd, out owner);
            if (owner is not null && owner._actions.TryGetValue((int)wParam, out var action))
            {
                // Captures open windows: run them outside this window procedure.
                owner._uiThread.Post(action);
                return 0;
            }
        }
        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
