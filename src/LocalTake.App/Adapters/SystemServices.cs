using Avalonia.Input;
using Avalonia.Threading;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;

namespace LocalTake.App.Adapters;

internal sealed class DispatcherUiThread : IUiThread
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>Images and text through the operating system's own clipboard.</summary>
internal sealed class ClipboardAdapter(INativeClipboard clipboard) : IClipboard
{
    public void SetImage(IImage image) => clipboard.SetImage(ImageData.Of(image).Pixels);

    public void SetText(string text) => clipboard.SetText(text);
}

/// <summary>Avalonia keys and the core's key codes.</summary>
internal static class AvaloniaKeys
{
    private static readonly Dictionary<Key, KeyCode> ToCore = Build();

    public static KeyCode ToKeyCode(Key key) => ToCore.GetValueOrDefault(key, KeyCode.None);

    public static HotkeyModifiers ToModifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? HotkeyModifiers.Control : 0) |
        (modifiers.HasFlag(KeyModifiers.Alt) ? HotkeyModifiers.Alt : 0) |
        (modifiers.HasFlag(KeyModifiers.Shift) ? HotkeyModifiers.Shift : 0) |
        (modifiers.HasFlag(KeyModifiers.Meta) ? HotkeyModifiers.Meta : 0);

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static Dictionary<Key, KeyCode> Build()
    {
        var map = new Dictionary<Key, KeyCode>();
        foreach (var code in Enum.GetValues<KeyCode>())
        {
            var key = code switch
            {
                KeyCode.None => Key.None,
                KeyCode.PrintScreen => Key.PrintScreen,
                KeyCode.Backspace => Key.Back,
                KeyCode.ScrollLock => Key.Scroll,
                KeyCode.Enter => Key.Enter,
                KeyCode.PageDown => Key.PageDown,
                KeyCode.PageUp => Key.PageUp,
                _ => Enum.Parse<Key>(code.ToString())
            };
            if (key != Key.None)
                map.TryAdd(key, code);
        }
        return map;
    }
}
