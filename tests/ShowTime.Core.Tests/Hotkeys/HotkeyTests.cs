using ShowTime.Core.Hotkeys;

namespace ShowTime.Core.Tests.Hotkeys;

public sealed class HotkeyTests
{
    public sealed record HotkeyCase(string Input, bool Ok, string? Formatted);

    /// <summary>
    /// Deliberate changes from v0.1.0. Enter and PageDown were written "Return" and "Next" (WPF enum aliases); a
    /// modifier key alone was accepted as the main key, which Windows can never trigger.
    /// </summary>
    private static readonly Dictionary<string, (bool Ok, string? Formatted)> Changes = new()
    {
        ["Ctrl+Enter"] = (true, "Ctrl+Enter"),
        ["Ctrl+Return"] = (true, "Ctrl+Enter"),
        ["Ctrl+PageDown"] = (true, "Ctrl+PageDown"),
        ["Ctrl+Next"] = (true, "Ctrl+PageDown"),
        ["Ctrl+Back"] = (true, "Ctrl+Backspace"),
        ["Ctrl+Scroll"] = (true, "Ctrl+ScrollLock"),
        ["Ctrl+LeftCtrl"] = (false, null),
    };

    public static TheoryData<string, bool, string?> LegacyCases()
    {
        var data = new TheoryData<string, bool, string?>();
        foreach (var c in Fixtures.Load<List<HotkeyCase>>("hotkeys.json"))
        {
            var (ok, formatted) = Changes.TryGetValue(c.Input, out var change) ? change : (c.Ok, c.Formatted);
            data.Add(c.Input, ok, formatted);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void TryParse_and_ToString_behave_as_in_v0_1_0(string input, bool ok, string? formatted)
    {
        Hotkey.TryParse(input, out var hotkey).ShouldBe(ok);
        if (ok)
            hotkey.ToString().ShouldBe(formatted);
    }

    [Theory]
    [InlineData("Ctrl+Backspace", "Ctrl+Backspace")]
    [InlineData("Ctrl+ScrollLock", "Ctrl+ScrollLock")]
    [InlineData("Cmd+Shift+A", "Shift+Win+A")]
    [InlineData("Command+Option+B", "Alt+Win+B")]
    [InlineData("Meta+C", "Win+C")]
    public void TryParse_accepts_the_new_names_and_the_macOS_modifiers(string input, string formatted)
    {
        Hotkey.TryParse(input, out var hotkey).ShouldBeTrue();
        hotkey.ToString().ShouldBe(formatted);
    }

    [Fact]
    public void Format_for_macOS_names_the_modifiers_as_on_a_Mac_keyboard()
    {
        Hotkey.TryParse("Ctrl+Alt+Shift+Win+K", out var hotkey).ShouldBeTrue();
        hotkey.Format(HotkeyNaming.MacOS).ShouldBe("Ctrl+Option+Shift+Cmd+K");
    }

    [Fact]
    public void A_macOS_formatted_hotkey_parses_back_to_the_same_value()
    {
        var hotkey = new Hotkey(HotkeyModifiers.Alt | HotkeyModifiers.Meta, KeyCode.F5);
        Hotkey.TryParse(hotkey.Format(HotkeyNaming.MacOS), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(hotkey);
    }

    [Theory]
    [InlineData(KeyCode.PrintScreen, true)]
    [InlineData(KeyCode.F1, true)]
    [InlineData(KeyCode.F24, true)]
    [InlineData(KeyCode.A, false)]
    [InlineData(KeyCode.Space, false)]
    public void Only_Print_Screen_and_F_keys_may_be_used_without_a_modifier(KeyCode key, bool allowed) =>
        Hotkey.AllowedWithoutModifier(key).ShouldBe(allowed);

    [Fact]
    public void IsValid_rejects_a_plain_letter() =>
        new Hotkey(HotkeyModifiers.None, KeyCode.A).IsValid.ShouldBeFalse();

    [Fact]
    public void A_plain_key_is_only_parsed_when_allowed()
    {
        Hotkey.TryParse("Space", out _).ShouldBeFalse();
        Hotkey.TryParse("Space", out var hotkey, allowPlainKey: true).ShouldBeTrue();
        hotkey.ShouldBe(new Hotkey(HotkeyModifiers.None, KeyCode.Space));
    }
}
