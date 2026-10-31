using ShowTime.Core.Hotkeys;

namespace ShowTime.Core.Recording;

/// <summary>The side buttons of a mouse, as Windows names them: usually Back (4) and Forward (5).</summary>
public enum SideMouseButton
{
    XButton1,
    XButton2
}

/// <summary>
/// What the user holds to highlight with the mouse during a recording: a shortcut or a side mouse button, written in
/// settings.json as "Ctrl+Space" or "XButton1". Any key is allowed alone, Space included, because the trigger is only
/// registered while a recording is under way.
/// </summary>
public readonly record struct HighlightTrigger
{
    private HighlightTrigger(Hotkey? key, SideMouseButton? button)
    {
        Key = key;
        Button = button;
    }

    public Hotkey? Key { get; }

    public SideMouseButton? Button { get; }

    /// <summary>A plain key that types text: while recording, it no longer reaches other applications.</summary>
    public bool BlocksTyping => Key is { Modifiers: HotkeyModifiers.None } key && !Hotkey.AllowedWithoutModifier(key.Key);

    public static HighlightTrigger FromKey(Hotkey key) => new(key, null);

    public static HighlightTrigger FromButton(SideMouseButton button) => new(null, button);

    public static bool TryParse(string? text, out HighlightTrigger trigger)
    {
        trigger = default;
        foreach (var button in Enum.GetValues<SideMouseButton>())
        {
            if (string.Equals(text?.Trim(), button.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                trigger = FromButton(button);
                return true;
            }
        }
        if (!Hotkey.TryParse(text, out var key, allowPlainKey: true))
            return false;
        trigger = FromKey(key);
        return true;
    }

    /// <summary>As the recording bar shows it.</summary>
    public string Format(HotkeyNaming naming) => Button switch
    {
        SideMouseButton.XButton1 => "Souris 4",
        SideMouseButton.XButton2 => "Souris 5",
        _ => Key?.Format(naming) ?? ""
    };

    /// <summary>As settings.json holds it.</summary>
    public override string ToString() => Button?.ToString() ?? Key?.ToString() ?? "";
}
