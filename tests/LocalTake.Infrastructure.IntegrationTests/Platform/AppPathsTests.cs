using LocalTake.Infrastructure.Platform;

namespace LocalTake.Infrastructure.IntegrationTests.Platform;

public sealed class AppPathsTests
{
    [Fact]
    public void Windows_output_folder_is_in_Pictures_when_it_is_not_synced() =>
        AppPaths.WindowsOutputFolder(@"C:\Users\Me\Pictures", @"C:\Users\Me", [@"C:\Users\Me\OneDrive"])
            .ShouldBe(Path.Combine(@"C:\Users\Me\Pictures", "LocalTake"));

    [Fact]
    public void Windows_output_folder_avoids_a_Pictures_folder_synced_by_OneDrive() =>
        AppPaths.WindowsOutputFolder(@"C:\Users\Me\OneDrive\Images", @"C:\Users\Me", [@"c:\users\me\onedrive"])
            .ShouldBe(Path.Combine(@"C:\Users\Me", "Captures LocalTake"));

    [Fact]
    public void Windows_output_folder_avoids_an_unknown_Pictures_folder() =>
        AppPaths.WindowsOutputFolder("", @"C:\Users\Me", []).ShouldBe(Path.Combine(@"C:\Users\Me", "Captures LocalTake"));

    [Fact]
    public void The_current_platform_has_absolute_paths()
    {
        var paths = AppPaths.ForCurrentPlatform();
        Path.IsPathFullyQualified(paths.SettingsFile).ShouldBeTrue();
        Path.IsPathFullyQualified(paths.LogsFolder).ShouldBeTrue();
        Path.IsPathFullyQualified(paths.DefaultOutputFolder).ShouldBeTrue();
        Path.GetFileName(paths.SettingsFile).ShouldBe("settings.json");
    }

    [Fact]
    public void On_macOS_settings_go_to_Application_Support()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        AppPaths.ForCurrentPlatform().SettingsFile.ShouldContain("Library/Application Support/LocalTake");
    }
}
