using System.IO.Abstractions.TestingHelpers;
using LocalTake.Core.Files;
using Microsoft.Extensions.Time.Testing;

namespace LocalTake.Core.Tests.Files;

public sealed class OutputPathAllocatorTests
{
    private static readonly string Folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LocalTakeOut");
    private readonly MockFileSystem _fs = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 7, 9, 5, 2, TimeSpan.Zero));
    private readonly OutputPathAllocator _allocator;

    public OutputPathAllocatorTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _allocator = new OutputPathAllocator(_fs, _clock);
    }

    [Fact]
    public void Next_expands_the_pattern_in_the_output_folder() =>
        _allocator.Next(Folder, "Shot_%NUM%", 7, ".png").ShouldBe(System.IO.Path.Combine(Folder, "Shot_000007.png"));

    [Fact]
    public void Next_uses_the_local_time() =>
        _allocator.Next(Folder, "%hh%-%mm%", 1, ".png").ShouldBe(System.IO.Path.Combine(Folder, "09-05.png"));

    [Fact]
    public void Next_creates_the_output_folder()
    {
        _allocator.Next(Folder, "x", 1, ".png");
        _fs.Directory.Exists(Folder).ShouldBeTrue();
    }

    [Fact]
    public void Next_numbers_a_name_already_taken()
    {
        _fs.AddFile(System.IO.Path.Combine(Folder, "x.png"), new MockFileData(""));
        _fs.AddFile(System.IO.Path.Combine(Folder, "x (2).png"), new MockFileData(""));
        _allocator.Next(Folder, "x", 1, ".png").ShouldBe(System.IO.Path.Combine(Folder, "x (3).png"));
    }

    [Fact]
    public void Next_ignores_a_file_with_the_same_name_and_another_extension()
    {
        _fs.AddFile(System.IO.Path.Combine(Folder, "x.jpg"), new MockFileData(""));
        _allocator.Next(Folder, "x", 1, ".png").ShouldBe(System.IO.Path.Combine(Folder, "x.png"));
    }
}
