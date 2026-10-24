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

    [Theory]
    [InlineData("/Applications/LocalTake.app/Contents/MonoBundle/", "/Applications/LocalTake.app/Contents/MacOS")]
    [InlineData("/Users/moi/Downloads/LocalTake 2.app/Contents/MonoBundle", "/Users/moi/Downloads/LocalTake 2.app/Contents/MacOS")]
    [InlineData("/private/var/folders/ab/T/AppTranslocation/1234/d/LocalTake.app/Contents/MonoBundle/",
        "/private/var/folders/ab/T/AppTranslocation/1234/d/LocalTake.app/Contents/MacOS")]
    public void The_bundled_ffmpeg_is_found_wherever_the_app_is(string assemblies, string expected) =>
        AppPaths.FfmpegDirectory(assemblies).ShouldBe(expected);

    [Theory]
    [InlineData(@"C:\Program Files\LocalTake\")]
    [InlineData("/Users/moi/LocalTake/src/LocalTake.Mac/bin/Debug/net10.0-macos/osx-arm64/")]
    public void Outside_a_bundle_the_assemblies_folder_is_searched(string assemblies) =>
        AppPaths.FfmpegDirectory(assemblies).ShouldBe(assemblies);

    [Fact]
    public void The_current_platform_has_absolute_paths()
    {
        var paths = AppPaths.ForCurrentPlatform();
        Path.IsPathFullyQualified(paths.SettingsFile).ShouldBeTrue();
        Path.IsPathFullyQualified(paths.LogsFolder).ShouldBeTrue();
        Path.IsPathFullyQualified(paths.DefaultOutputFolder).ShouldBeTrue();
        Path.IsPathFullyQualified(paths.WorkFolder).ShouldBeTrue();
        Path.GetFileName(paths.SettingsFile).ShouldBe("settings.json");
    }

    [Fact]
    public void The_model_shipped_next_to_LocalTake_is_looked_for_first()
    {
        AppPaths.ForCurrentPlatform().ModelsFolders[0].ShouldBe(Path.Combine(AppContext.BaseDirectory, "models"));
    }

    [Fact]
    public void On_macOS_settings_go_to_Application_Support()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS only.");
        AppPaths.ForCurrentPlatform().SettingsFile.ShouldContain("Library/Application Support/LocalTake");
    }
}
