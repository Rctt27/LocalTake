namespace LocalTake.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    /// <summary>The Windows key, or Command on a Mac.</summary>
    Meta = 8
}

/// <summary>How modifiers are written: as on a PC keyboard (also the settings file form) or as on a Mac.</summary>
public enum HotkeyNaming
{
    Windows,
    MacOS
}

/// <summary>A global shortcut written as "Ctrl+Alt+PrintScreen".</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, KeyCode Key)
{
    /// <summary>Names accepted besides <see cref="KeyCode"/>'s own, mostly the WPF names v0.1.0 wrote.</summary>
    private static readonly Dictionary<string, KeyCode> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrtSc"] = KeyCode.PrintScreen,
        ["PrtScrn"] = KeyCode.PrintScreen,
        ["Snapshot"] = KeyCode.PrintScreen,
        ["Return"] = KeyCode.Enter,
        ["Prior"] = KeyCode.PageUp,
        ["Next"] = KeyCode.PageDown,
        ["Back"] = KeyCode.Backspace,
        ["Scroll"] = KeyCode.ScrollLock
    };

    /// <summary>A key, with a modifier unless the key may be used alone.</summary>
    public bool IsValid => Key != KeyCode.None && (Modifiers != HotkeyModifiers.None || AllowedWithoutModifier(Key));

    public static bool TryParse(string? text, out Hotkey hotkey) => TryParse(text, out hotkey, allowPlainKey: false);

    /// <param name="allowPlainKey">Accepts any key without a modifier, for a shortcut only registered briefly.</param>
    public static bool TryParse(string? text, out Hotkey hotkey, bool allowPlainKey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = ParseModifier(parts[i]);
            if (modifier == HotkeyModifiers.None)
                return false;
            modifiers |= modifier;
        }

        var candidate = new Hotkey(modifiers, ParseKey(parts[^1]));
        if (candidate.Key == KeyCode.None || !(allowPlainKey || candidate.IsValid))
            return false;
        hotkey = candidate;
        return true;
    }

    /// <summary>
    /// A plain letter, Enter or Space registered system-wide would stop working in every other application.
    /// </summary>
    public static bool AllowedWithoutModifier(KeyCode key) => key is KeyCode.PrintScreen or (>= KeyCode.F1 and <= KeyCode.F24);

    public string Format(HotkeyNaming naming)
    {
        var mac = naming == HotkeyNaming.MacOS;
        var text = "";
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
            text += "Ctrl+";
        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            text += mac ? "Option+" : "Alt+";
        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            text += "Shift+";
        if (Modifiers.HasFlag(HotkeyModifiers.Meta))
            text += mac ? "Cmd+" : "Win+";
        return text + KeyName(Key);
    }

    public override string ToString() => Format(HotkeyNaming.Windows);

    private static HotkeyModifiers ParseModifier(string name) => name.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotkeyModifiers.Control,
        "ALT" or "OPTION" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" or "WINDOWS" or "CMD" or "COMMAND" or "META" => HotkeyModifiers.Meta,
        _ => HotkeyModifiers.None
    };

    private static KeyCode ParseKey(string name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            return KeyCode.D0 + (name[0] - '0');
        if (Aliases.TryGetValue(name, out var alias))
            return alias;
        // Enum.TryParse also accepts numbers ("12"), which are not key names.
        return char.IsAsciiLetter(name[0]) && Enum.TryParse<KeyCode>(name, ignoreCase: true, out var key) ? key : KeyCode.None;
    }

    private static string KeyName(KeyCode key) =>
        key is >= KeyCode.D0 and <= KeyCode.D9 ? ((char)('0' + (key - KeyCode.D0))).ToString() : key.ToString();
}
