using System.Runtime.Versioning;
using Microsoft.Win32;
using ShowTime.Core.Geometry;
using ShowTime.Core.Hotkeys;
using ShowTime.Core.Ports;
using ShowTime.Platform.Windows;

namespace ShowTime.Platform.Tests.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformTests
{
    public WindowsPlatformTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");

    private sealed class ImmediateUiThread : IUiThread
    {
        public void Post(Action action) => action();
    }

    [Fact]
    public void Every_key_code_has_its_own_virtual_key()
    {
        var codes = Enum.GetValues<KeyCode>().Where(k => k != KeyCode.None).ToList();
        var keys = codes.Select(WindowsKeys.VirtualKey).ToList();
        keys.ShouldAllBe(vk => vk > 0);
        keys.Distinct().Count().ShouldBe(codes.Count);
        WindowsKeys.VirtualKey(KeyCode.PrintScreen).ShouldBe(0x2Cu);
        WindowsKeys.VirtualKey(KeyCode.A).ShouldBe(0x41u);
        WindowsKeys.VirtualKey(KeyCode.F24).ShouldBe(0x87u);
    }

    [Fact]
    public void Modifiers_map_to_RegisterHotKey_flags() =>
        WindowsKeys.Modifiers(HotkeyModifiers.Control | HotkeyModifiers.Meta).ShouldBe(0x2u | 0x8u);

    [Fact]
    public void A_shortcut_can_be_registered_once_and_released()
    {
        Hotkey.TryParse("Ctrl+Alt+Shift+F23", out var hotkey).ShouldBeTrue();
        using var first = new WindowsHotkeys(new ImmediateUiThread());
        using var second = new WindowsHotkeys(new ImmediateUiThread());

        first.Register(hotkey, () => { }).ShouldBeTrue();
        second.Register(hotkey, () => { }).ShouldBeFalse();
        first.UnregisterAll();
        second.Register(hotkey, () => { }).ShouldBeTrue();
    }

    [Fact]
    public void Grab_returns_opaque_pixels_of_the_requested_size()
    {
        var image = new WindowsScreenGrabber().Grab(new PixelRect(0, 0, 32, 16));
        image.Width.ShouldBe(32);
        image.Height.ShouldBe(16);
        Enumerable.Range(0, 32 * 16).All(i => image.Pixels[i * 4 + 3] == 255).ShouldBeTrue();
    }

    [Fact]
    public void Visible_windows_have_a_size() =>
        new WindowsScreenGrabber().VisibleWindows().ShouldAllBe(w => w.Width > 1 && w.Height > 1);

    [Fact]
    public void Clipboard_keeps_an_image_and_a_text()
    {
        var clipboard = new WindowsClipboard();
        var pixels = new byte[3 * 2 * 4];
        for (var i = 0; i < 6; i++)
        {
            pixels[i * 4] = (byte)(10 * i);
            pixels[i * 4 + 1] = 100;
            pixels[i * 4 + 2] = 200;
            pixels[i * 4 + 3] = 255;
        }

        clipboard.SetImage(new PixelBuffer(3, 2, pixels));
        var read = clipboard.GetImage();
        read.ShouldNotBeNull();
        read.Width.ShouldBe(3);
        read.Height.ShouldBe(2);
        read.Pixels.ShouldBe(pixels);

        clipboard.SetText("ShowTime é");
        clipboard.GetImage().ShouldBeNull();
    }

    [Fact]
    public void Startup_registration_writes_and_removes_its_value()
    {
        const string key = @"Software\ShowTimeTests\Run";
        try
        {
            var startup = new WindowsStartupRegistration(key, "ShowTime", @"C:\Apps\ShowTime.exe");
            startup.IsEnabled.ShouldBeFalse();
            startup.SetEnabled(true);
            startup.IsEnabled.ShouldBeTrue();
            Registry.CurrentUser.OpenSubKey(key)!.GetValue("ShowTime").ShouldBe("\"C:\\Apps\\ShowTime.exe\"");
            startup.SetEnabled(false);
            startup.IsEnabled.ShouldBeFalse();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\ShowTimeTests", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void A_file_sent_to_the_Recycle_Bin_leaves_its_folder()
    {
        // Only on CI: locally it would fill the developer's Recycle Bin.
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CI") == "true", "Runs on CI only.");
        var file = Path.Combine(Path.GetTempPath(), "ShowTime-trash-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "x");
        new WindowsFileTrash().MoveToTrash(file);
        File.Exists(file).ShouldBeFalse();
    }
}
