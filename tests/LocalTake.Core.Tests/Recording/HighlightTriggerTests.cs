using LocalTake.Core.Hotkeys;
using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class HighlightTriggerTests
{
    [Fact]
    public void A_combination_is_a_key_trigger()
    {
        HighlightTrigger.TryParse("Ctrl+Space", out var trigger).ShouldBeTrue();
        trigger.Key.ShouldBe(new Hotkey(HotkeyModifiers.Control, KeyCode.Space));
        trigger.Button.ShouldBeNull();
        trigger.BlocksTyping.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Space", true)]
    [InlineData("A", true)]
    [InlineData("F13", false)]
    public void A_plain_key_is_allowed_and_says_whether_it_types(string text, bool blocksTyping)
    {
        HighlightTrigger.TryParse(text, out var trigger).ShouldBeTrue();
        trigger.Key!.Value.Modifiers.ShouldBe(HotkeyModifiers.None);
        trigger.BlocksTyping.ShouldBe(blocksTyping);
    }

    [Theory]
    [InlineData("XButton1", SideMouseButton.XButton1, "Souris 4")]
    [InlineData("xbutton2", SideMouseButton.XButton2, "Souris 5")]
    public void Side_buttons_are_mouse_triggers(string text, SideMouseButton button, string shown)
    {
        HighlightTrigger.TryParse(text, out var trigger).ShouldBeTrue();
        trigger.Button.ShouldBe(button);
        trigger.Key.ShouldBeNull();
        trigger.BlocksTyping.ShouldBeFalse();
        trigger.Format(HotkeyNaming.Windows).ShouldBe(shown);
        trigger.ToString().ShouldBe(button.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Ctrl+")]
    [InlineData("XButton3")]
    [InlineData("Foo")]
    public void Anything_else_is_refused(string? text) => HighlightTrigger.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void Keys_are_shown_in_the_platform_naming()
    {
        HighlightTrigger.TryParse("Ctrl+Alt+Space", out var trigger).ShouldBeTrue();
        trigger.Format(HotkeyNaming.MacOS).ShouldBe("Ctrl+Option+Space");
        trigger.ToString().ShouldBe("Ctrl+Alt+Space");
    }

    [Fact]
    public void The_factories_build_the_same_triggers_as_parsing()
    {
        HighlightTrigger.TryParse("Ctrl+Space", out var key).ShouldBeTrue();
        key.ShouldBe(HighlightTrigger.FromKey(new Hotkey(HotkeyModifiers.Control, KeyCode.Space)));
        HighlightTrigger.TryParse("XButton1", out var button).ShouldBeTrue();
        button.ShouldBe(HighlightTrigger.FromButton(SideMouseButton.XButton1));
    }
}
