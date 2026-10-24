using System.Text.Json;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Settings;

namespace LocalTake.Core.Tests.Settings;

public sealed class SettingsDefaultsTests
{
    private static IEnumerable<string> Shortcuts(SettingsDefaults d) =>
    [
        d.HotkeyRegion, d.HotkeyWindow, d.HotkeyFullScreen, d.HotkeyLastRegion, d.HotkeyVideoRegion,
        d.HotkeyVideoFullScreen, d.HotkeyVideoStart, d.HotkeyVideoStop, d.HighlightTrigger
    ];

    [Fact]
    public void Windows_defaults_are_the_shortcuts_of_v0_1_0()
    {
        using var legacy = JsonDocument.Parse(File.ReadAllText(Fixtures.Path("settings-v0.1.0-defaults.json")));
        var defaults = SettingsDefaults.ForWindows("C:\\Out");
        var settings = new AppSettings(defaults);

        foreach (var property in legacy.RootElement.EnumerateObject().Where(p => p.Name.StartsWith("Hotkey", StringComparison.Ordinal)))
            typeof(AppSettings).GetProperty(property.Name)!.GetValue(settings).ShouldBe(property.Value.GetString(), property.Name);
    }

    [Fact]
    public void macOS_defaults_are_valid_shortcuts_without_Print_Screen()
    {
        var defaults = SettingsDefaults.ForMacOS("/Users/me/Pictures/LocalTake");
        foreach (var text in Shortcuts(defaults).Where(s => s.Length > 0))
        {
            Hotkey.TryParse(text, out var hotkey).ShouldBeTrue(text);
            hotkey.Key.ShouldNotBe(KeyCode.PrintScreen);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Each_platform_has_distinct_shortcuts(bool mac)
    {
        var defaults = mac ? SettingsDefaults.ForMacOS("x") : SettingsDefaults.ForWindows("x");
        var assigned = Shortcuts(defaults).Where(s => s.Length > 0).ToList();
        assigned.Distinct().Count().ShouldBe(assigned.Count);
    }

    [Fact]
    public void Both_platforms_highlight_with_Ctrl_Space()
    {
        SettingsDefaults.ForWindows("x").HighlightTrigger.ShouldBe("Ctrl+Space");
        SettingsDefaults.ForMacOS("x").HighlightTrigger.ShouldBe("Ctrl+Space");
        new AppSettings(SettingsDefaults.ForWindows("x")).HighlightTrigger.ShouldBe("Ctrl+Space");
    }

    [Fact]
    public void New_settings_take_the_platform_folder() =>
        new AppSettings(SettingsDefaults.ForMacOS("/Users/me/Pictures/LocalTake")).OutputFolder.ShouldBe("/Users/me/Pictures/LocalTake");

    [Theory]
    [InlineData("png", ".png")]
    [InlineData("jpg", ".jpg")]
    [InlineData("bmp", ".png")]
    public void ImageExtension_follows_the_format(string format, string extension) =>
        new AppSettings(SettingsDefaults.ForWindows("x")) { Format = format }.ImageExtension.ShouldBe(extension);
}
