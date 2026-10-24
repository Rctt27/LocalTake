using System.Runtime.InteropServices;
using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Platform.Windows;

namespace LocalTake.Windows.Tests.Recording;

/// <summary>
/// The trigger is pressed with SendInput, which goes through RegisterHotKey and low-level hooks like a real key. The
/// input is registered on the STA thread, whose dispatcher pumps the messages both need.
/// </summary>
public sealed partial class WindowsHighlightInputTests : IDisposable
{
    private const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkF23 = 0x86;
    private const uint InputMouse = 0, InputKeyboard = 1, KeyUp = 0x2, MouseXDown = 0x80, MouseXUp = 0x100, XButton2 = 2;

    private readonly WindowsHighlightInput _input = Sta.Run(() => new WindowsHighlightInput(new ImmediateUiThread())).GetAwaiter().GetResult();

    public void Dispose()
    {
        Release();
        Sta.Run(_input.Dispose).GetAwaiter().GetResult();
    }

    private sealed class ImmediateUiThread : IUiThread
    {
        public void Post(Action action) => action();
    }

    private static HighlightTrigger Trigger(string text)
    {
        HighlightTrigger.TryParse(text, out var trigger).ShouldBeTrue();
        return trigger;
    }

    private Task<bool> RegisterAsync(string trigger, Action pressed) => Sta.Run(() => _input.Register(Trigger(trigger), pressed));

    /// <summary>Lets the STA thread handle what was sent: WM_HOTKEY, the hook.</summary>
    private static Task Pump() => Sta.Run(Sta.DoEvents);

    [Fact]
    public async Task A_held_shortcut_is_reported_until_released()
    {
        var pressed = 0;
        (await RegisterAsync("Ctrl+Alt+Shift+F23", () => pressed++)).ShouldBeTrue();
        Keys(down: true, VkControl, VkMenu, VkShift, VkF23);
        await Pump();
        pressed.ShouldBe(1);
        _input.IsHeld.ShouldBeTrue();

        Keys(down: false, VkF23);
        await Pump();
        _input.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Releasing_a_modifier_first_ends_the_hold()
    {
        (await RegisterAsync("Ctrl+Alt+Shift+F23", () => { })).ShouldBeTrue();
        Keys(down: true, VkControl, VkMenu, VkShift, VkF23);
        await Pump();
        _input.IsHeld.ShouldBeTrue();

        Keys(down: false, VkControl);
        await Pump();
        _input.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task A_side_button_is_reported_held_until_released()
    {
        var pressed = 0;
        (await RegisterAsync("XButton2", () => pressed++)).ShouldBeTrue();
        Mouse(MouseXDown);
        await Pump();
        pressed.ShouldBe(1);
        _input.IsHeld.ShouldBeTrue();

        Mouse(MouseXUp);
        await Pump();
        _input.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Unregistering_frees_the_shortcut()
    {
        (await RegisterAsync("Ctrl+Alt+Shift+F23", () => { })).ShouldBeTrue();
        await Sta.Run(_input.Unregister);
        Hotkey.TryParse("Ctrl+Alt+Shift+F23", out var hotkey).ShouldBeTrue();
        await Sta.Run(() =>
        {
            using var other = new WindowsHotkeys(new ImmediateUiThread());
            other.Register(hotkey, () => { }).ShouldBeTrue();
        });
    }

    [Fact]
    public void Cursor_is_the_mouse_position()
    {
        GetCursorPos(out var before).ShouldBeTrue();
        try
        {
            SetCursorPos(10, 20).ShouldBeTrue();
            _input.Cursor.ShouldBe(new PixelPoint(10, 20));
        }
        finally
        {
            SetCursorPos(before.X, before.Y);
        }
    }

    private static void Keys(bool down, params ushort[] keys)
    {
        foreach (var key in keys)
            Send(new Input { Type = InputKeyboard, Union = new InputUnion { Keyboard = new KeyboardInput { Vk = key, Flags = down ? 0 : KeyUp } } });
    }

    private static void Mouse(uint flags) =>
        Send(new Input { Type = InputMouse, Union = new InputUnion { Mouse = new MouseInput { MouseData = XButton2, Flags = flags } } });

    /// <summary>Lets nothing stay pressed after a test, whatever failed.</summary>
    private static void Release()
    {
        Keys(down: false, VkF23, VkShift, VkMenu, VkControl);
        Mouse(MouseXUp);
    }

    private static unsafe void Send(Input input)
    {
        if (SendInput(1, &input, sizeof(Input)) != 1)
            Assert.Skip("SendInput is refused: no interactive desktop.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [LibraryImport("user32.dll")]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);
}
