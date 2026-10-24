namespace LocalTake.Core.Settings;

/// <summary>The settings that depend on the platform: output folder and shortcuts.</summary>
public sealed record SettingsDefaults(
    string OutputFolder,
    string HotkeyRegion,
    string HotkeyWindow,
    string HotkeyFullScreen,
    string HotkeyLastRegion,
    string HotkeyVideoRegion,
    string HotkeyVideoFullScreen,
    string HotkeyVideoStart,
    string HotkeyVideoStop,
    string HighlightTrigger)
{
    /// <summary>The shortcuts of v0.1.0, built around Print Screen as in TinyTake.</summary>
    public static SettingsDefaults ForWindows(string outputFolder) => new(
        outputFolder,
        HotkeyRegion: "PrintScreen",
        HotkeyWindow: "Alt+PrintScreen",
        HotkeyFullScreen: "Ctrl+PrintScreen",
        HotkeyLastRegion: "",
        HotkeyVideoRegion: "Ctrl+Shift+PrintScreen",
        HotkeyVideoFullScreen: "",
        HotkeyVideoStart: "Ctrl+R",
        HotkeyVideoStop: "Ctrl+D",
        HighlightTrigger: "Ctrl+Space");

    /// <summary>
    /// A Mac has no Print Screen key, and Cmd+Shift+3/4/5 belong to macOS: Ctrl+Shift with a digit is free in macOS
    /// and in common applications.
    /// </summary>
    public static SettingsDefaults ForMacOS(string outputFolder) => new(
        outputFolder,
        HotkeyRegion: "Ctrl+Shift+1",
        HotkeyWindow: "Ctrl+Shift+2",
        HotkeyFullScreen: "Ctrl+Shift+3",
        HotkeyLastRegion: "",
        HotkeyVideoRegion: "Ctrl+Shift+4",
        HotkeyVideoFullScreen: "",
        HotkeyVideoStart: "Ctrl+Shift+R",
        HotkeyVideoStop: "Ctrl+Shift+D",
        HighlightTrigger: "Ctrl+Space");
}
