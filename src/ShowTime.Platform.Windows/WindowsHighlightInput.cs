using System.Runtime.InteropServices;
using ShowTime.Core.Geometry;
using ShowTime.Core.Hotkeys;
using ShowTime.Core.Ports;
using ShowTime.Core.Recording;

namespace ShowTime.Platform.Windows;

/// <summary>
/// The highlight trigger on Windows. A key goes through RegisterHotKey, which only reports the press: whether it is
/// still held is read with GetAsyncKeyState, which needs no hook. A side mouse button needs a low-level mouse hook,
/// which also swallows the click so that a browser does not go back a page. Created and used on the UI thread, whose
/// message loop both need.
/// </summary>
public sealed unsafe class WindowsHighlightInput : IHighlightInput, IDisposable
{
    /// <summary>The instance whose button the hook watches; one at a time.</summary>
    private static WindowsHighlightInput? _hooked;

    private readonly IUiThread _uiThread;
    private WindowsHotkeys? _hotkeys;
    private HighlightTrigger? _trigger;
    private Action? _pressed;
    private nint _hook;
    private SideMouseButton? _hookedButton;
    private volatile bool _buttonHeld;

    /// <summary>Unregistered while the button was held: the hook stays to swallow its release.</summary>
    private bool _unhookOnRelease;

    public WindowsHighlightInput(IUiThread uiThread) => _uiThread = uiThread;

    public bool IsHeld => _trigger switch
    {
        { Key: { } key } => IsDown(WindowsKeys.VirtualKey(key.Key)) && ModifiersDown(key.Modifiers),
        { Button: not null } => _buttonHeld,
        _ => false
    };

    public PixelPoint Cursor => Win32.GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : default;

    public bool Register(HighlightTrigger trigger, Action pressed)
    {
        Unregister();
        if (trigger.Key is { } key)
        {
            _hotkeys ??= new WindowsHotkeys(_uiThread);
            if (!_hotkeys.Register(key, pressed))
                return false;
        }
        else
        {
            if (_hooked is not null && _hooked != this)
                return false;
            if (_hook == 0)
            {
                _hook = Win32.SetWindowsHookExW(Win32.WH_MOUSE_LL, &MouseProc, Win32.GetModuleHandleW(null), 0);
                if (_hook == 0)
                    return false;
                _hooked = this;
            }
            _hookedButton = trigger.Button;
            _unhookOnRelease = false;
        }
        _trigger = trigger;
        _pressed = pressed;
        return true;
    }

    public void Unregister()
    {
        _hotkeys?.UnregisterAll();
        _trigger = null;
        _pressed = null;
        // Released without the hook, the button would reach the window under the cursor: a browser goes back a page.
        if (_buttonHeld)
            _unhookOnRelease = true;
        else
            RemoveHook();
    }

    public void Dispose()
    {
        Unregister();
        RemoveHook();
        _hotkeys?.Dispose();
        _hotkeys = null;
    }

    private void RemoveHook()
    {
        if (_hook != 0)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = 0;
            _hooked = null;
        }
        _hookedButton = null;
        _buttonHeld = false;
        _unhookOnRelease = false;
    }

    private void RemoveHookOnceReleased()
    {
        if (_unhookOnRelease && !_buttonHeld)
            RemoveHook();
    }

    private static bool IsDown(uint virtualKey) => (Win32.GetAsyncKeyState((int)virtualKey) & 0x8000) != 0;

    private static bool ModifiersDown(HotkeyModifiers modifiers) =>
        (!modifiers.HasFlag(HotkeyModifiers.Control) || IsDown(Win32.VK_CONTROL)) &&
        (!modifiers.HasFlag(HotkeyModifiers.Alt) || IsDown(Win32.VK_MENU)) &&
        (!modifiers.HasFlag(HotkeyModifiers.Shift) || IsDown(Win32.VK_SHIFT)) &&
        (!modifiers.HasFlag(HotkeyModifiers.Meta) || IsDown(Win32.VK_LWIN) || IsDown(Win32.VK_RWIN));

    /// <summary>Must return quickly, or Windows skips the hook: it only notes the state and posts the rest.</summary>
    [UnmanagedCallersOnly]
    private static nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam is Win32.WM_XBUTTONDOWN or Win32.WM_XBUTTONUP
            && _hooked is { _hookedButton: { } button } owner)
        {
            var which = ((Win32.MSLLHOOKSTRUCT*)lParam)->mouseData >> 16;
            if (which == (button == SideMouseButton.XButton1 ? Win32.XBUTTON1 : Win32.XBUTTON2))
            {
                if (wParam != Win32.WM_XBUTTONDOWN)
                {
                    owner._buttonHeld = false;
                    if (owner._unhookOnRelease)
                        owner._uiThread.Post(owner.RemoveHookOnceReleased);
                }
                else if (!owner._buttonHeld)
                {
                    owner._buttonHeld = true;
                    if (owner._pressed is { } pressed)
                        owner._uiThread.Post(pressed);
                }
                return 1;
            }
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }
}
