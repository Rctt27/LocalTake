// Managed parts (key table) are checked on every OS; native calls are skipped outside macOS.
#pragma warning disable CA1416

using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Platform.MacOS;

namespace LocalTake.Platform.Tests.MacOS;

/// <summary>What can be checked anywhere: tables. The Carbon calls run on the macOS CI runner only.</summary>
public sealed class MacPlatformTests
{
    [Fact]
    public void Every_key_of_a_Mac_keyboard_has_its_own_code()
    {
        var unsupported = new[] { KeyCode.None, KeyCode.PrintScreen, KeyCode.Pause, KeyCode.ScrollLock, KeyCode.F21, KeyCode.F22, KeyCode.F23, KeyCode.F24 };
        var codes = new List<uint>();
        foreach (var key in Enum.GetValues<KeyCode>())
        {
            MacKeys.TryGetKeyCode(key, out var code).ShouldBe(!unsupported.Contains(key), key.ToString());
            if (!unsupported.Contains(key))
                codes.Add(code);
        }
        codes.Distinct().Count().ShouldBe(codes.Count);
    }

    [Fact]
    public void Modifiers_map_to_Carbon_masks() =>
        MacKeys.Modifiers(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Meta).ShouldBe(0x1B00u);

    [Fact]
    public void Every_default_macOS_shortcut_can_be_registered()
    {
        var d = SettingsDefaultsFor();
        foreach (var text in new[] { d.HotkeyRegion, d.HotkeyWindow, d.HotkeyFullScreen, d.HotkeyVideoRegion, d.HotkeyVideoStart, d.HotkeyVideoStop, d.HighlightTrigger })
        {
            Hotkey.TryParse(text, out var hotkey).ShouldBeTrue();
            MacKeys.TryGetKeyCode(hotkey.Key, out _).ShouldBeTrue(text);
        }
    }

    [Fact]
    public void A_shortcut_can_be_registered_with_a_release_action()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        Hotkey.TryParse("Ctrl+Alt+Shift+F19", out var hotkey).ShouldBeTrue();
        using var hotkeys = new MacHotkeys(new ImmediateUiThread());
        hotkeys.Register(hotkey, () => { }, () => { }).ShouldBeTrue();
        hotkeys.UnregisterAll();
    }

    [Fact]
    public void A_shortcut_can_be_registered_and_released()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        Hotkey.TryParse("Ctrl+Alt+Shift+F19", out var hotkey).ShouldBeTrue();
        using var hotkeys = new MacHotkeys(new ImmediateUiThread());
        hotkeys.Register(hotkey, () => { }).ShouldBeTrue();
        hotkeys.UnregisterAll();
    }

    [Theory]
    [InlineData(KeyCode.D1, "1")]
    [InlineData(KeyCode.R, "r")]
    [InlineData(KeyCode.F1, "")]
    [InlineData(KeyCode.F12, "")]
    [InlineData(KeyCode.Space, " ")]
    public void Menu_shows_a_shortcut_with_its_key_equivalent(KeyCode key, string equivalent) =>
        MacKeys.KeyEquivalent(key).ShouldBe(equivalent);

    [Fact]
    public void Every_Mac_key_code_reads_back_as_its_key()
    {
        foreach (var key in Enum.GetValues<KeyCode>())
            if (MacKeys.TryGetKeyCode(key, out var code))
                MacKeys.FromKeyCode(code).ShouldBe(key);
    }

    [Fact]
    public void A_key_LocalTake_does_not_know_reads_as_none() => MacKeys.FromKeyCode(0x3F).ShouldBe(KeyCode.None);

    [Fact]
    public void Key_without_an_equivalent_shows_none() => MacKeys.KeyEquivalent(KeyCode.PrintScreen).ShouldBeNull();

    [Fact]
    public void A_shortcut_already_registered_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        Hotkey.TryParse("Ctrl+Alt+Shift+F18", out var hotkey).ShouldBeTrue();
        using var first = new MacHotkeys(new ImmediateUiThread());
        using var second = new MacHotkeys(new ImmediateUiThread());

        first.Register(hotkey, () => { }).ShouldBeTrue();
        second.Register(hotkey, () => { }).ShouldBeFalse();
    }

    [Fact]
    public void A_shortcut_of_macOS_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        // Cmd+Shift+3 takes a screenshot, unless turned off in System Settings > Keyboard > Keyboard Shortcuts.
        Assert.SkipUnless(MacKeys.IsSystemShortcut(0x14, MacKeys.Modifiers(HotkeyModifiers.Meta | HotkeyModifiers.Shift)), "Turned off on this Mac.");
        Hotkey.TryParse("Cmd+Shift+3", out var hotkey).ShouldBeTrue();
        using var hotkeys = new MacHotkeys(new ImmediateUiThread());

        hotkeys.Register(hotkey, () => { }).ShouldBeFalse();
    }

    [Fact]
    public void Windows_on_screen_have_a_size()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        MacWindowList.OnScreen().ShouldAllBe(w => w.Width >= 0 && w.Height >= 0);
        ActiveDisplays.List().ShouldNotBeEmpty();
        _ = ScreenCaptureAccess.IsGranted;
    }

    private static Core.Settings.SettingsDefaults SettingsDefaultsFor() => Core.Settings.SettingsDefaults.ForMacOS("/x");

    private sealed class ImmediateUiThread : IUiThread
    {
        public void Post(Action action) => action();
    }
}
