// Managed parts (key table) are checked on every OS; native calls are skipped outside macOS.
#pragma warning disable CA1416

using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Platform.MacOS;

namespace LocalTake.Platform.Tests.MacOS;

/// <summary>What can be checked anywhere: tables. The native calls run on the macOS CI runner only.</summary>
public sealed class MacPlatformTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LocalTakeMac", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

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
    public void A_shortcut_can_be_registered_and_released()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        Hotkey.TryParse("Ctrl+Alt+Shift+F19", out var hotkey).ShouldBeTrue();
        using var hotkeys = new MacHotkeys(new ImmediateUiThread());
        hotkeys.Register(hotkey, () => { }).ShouldBeTrue();
        hotkeys.UnregisterAll();
    }

    [Fact]
    public void The_main_display_scale_is_known()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        MacScreenGrabber.MainDisplayScale().ShouldBeGreaterThanOrEqualTo(1);
        new MacScreenGrabber().VisibleWindows().ShouldAllBe(w => w.Width > 1);
    }

    [Fact]
    public void A_capture_has_the_requested_size_when_the_permission_is_granted()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        try
        {
            var image = new MacScreenGrabber().Grab(new PixelRect(0, 0, 64, 32));
            image.Width.ShouldBe(64);
            image.Height.ShouldBe(32);
        }
        catch (InvalidOperationException ex)
        {
            Assert.Skip("Screen Recording permission not granted on this runner: " + ex.Message);
        }
    }

    private static Core.Settings.SettingsDefaults SettingsDefaultsFor() => Core.Settings.SettingsDefaults.ForMacOS("/x");

    private sealed class ImmediateUiThread : IUiThread
    {
        public void Post(Action action) => action();
    }
}
