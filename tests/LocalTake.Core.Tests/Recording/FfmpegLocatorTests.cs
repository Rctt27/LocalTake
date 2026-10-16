using System.IO.Abstractions.TestingHelpers;
using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class FfmpegLocatorTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "LocalTakeFfmpeg");
    private static readonly string AppDir = Path.Combine(Root, "app");
    private static readonly string BinA = Path.Combine(Root, "a");
    private static readonly string BinB = Path.Combine(Root, "b");
    private static readonly string Homebrew = Path.Combine(Root, "homebrew");

    private readonly MockFileSystem _fs = new();

    private FfmpegLocator Locator(string? path = null, params string[] wellKnown) =>
        new(_fs, AppDir, path ?? string.Join(Path.PathSeparator, BinA, BinB), "ffmpeg.exe", wellKnown);

    private string Add(string directory)
    {
        var file = Path.Combine(directory, "ffmpeg.exe");
        _fs.AddFile(file, new MockFileData(""));
        return file;
    }

    [Fact]
    public void A_configured_path_wins_when_it_exists()
    {
        var configured = Add(Path.Combine(Root, "custom"));
        Add(AppDir);
        Locator().Find(configured).ShouldBe(configured);
    }

    [Fact]
    public void A_configured_path_that_does_not_exist_finds_nothing_rather_than_another_ffmpeg()
    {
        Add(AppDir);
        Locator().Find(Path.Combine(Root, "missing", "ffmpeg.exe")).ShouldBeNull();
    }

    [Fact]
    public void Without_a_configured_path_the_one_next_to_the_app_comes_first()
    {
        var nextToApp = Add(AppDir);
        Add(BinA);
        Locator().Find("").ShouldBe(nextToApp);
    }

    [Fact]
    public void Then_the_PATH_is_searched_in_order()
    {
        Add(BinB);
        var first = Add(BinA);
        Locator().Find("  ").ShouldBe(first);
    }

    [Fact]
    public void Quoted_and_malformed_PATH_entries_are_tolerated()
    {
        var found = Add(BinB);
        Locator($"\"{BinA}\"{Path.PathSeparator}bad\0dir{Path.PathSeparator} \"{BinB}\" ").Find("").ShouldBe(found);
    }

    [Fact]
    public void Well_known_folders_are_searched_after_the_PATH()
    {
        // An app started from the Finder does not get the shell's PATH, where Homebrew is added.
        var brew = Add(Homebrew);
        Locator(path: "", Homebrew).Find("").ShouldBe(brew);
    }

    [Fact]
    public void Nothing_found_returns_null() => Locator().Find("").ShouldBeNull();
}
