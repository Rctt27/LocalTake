namespace LocalTake.Core.Hotkeys;

/// <summary>
/// The keys a global shortcut can use, independent of any UI framework. Each platform maps them to its own codes
/// (Windows virtual keys, macOS key codes). The names are the ones written in settings.json.
/// </summary>
public enum KeyCode
{
    None,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
#pragma warning disable CA1720 // "Decimal" is the numeric keypad key, as written in settings.json.
    Multiply, Add, Subtract, Decimal, Divide,
#pragma warning restore CA1720
    PrintScreen, Pause, ScrollLock, Insert, Delete, Home, End, PageUp, PageDown,
    Left, Up, Right, Down,
    Space, Enter, Tab, Escape, Backspace,
    OemPlus, OemMinus, OemComma, OemPeriod
}
