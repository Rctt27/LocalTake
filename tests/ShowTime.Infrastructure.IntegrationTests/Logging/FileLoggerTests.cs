using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ShowTime.Infrastructure.Logging;

namespace ShowTime.Infrastructure.IntegrationTests.Logging;

public sealed class FileLoggerTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 16, 14, 30, 5, TimeSpan.Zero));

    public void Dispose() => _folder.Dispose();

    private string LogFile(string day) => Path.Combine(_folder.Path, $"showtime-{day}.log");

    [Fact]
    public void Entries_are_appended_to_a_file_per_day_with_level_and_category()
    {
        using (var provider = new FileLoggerProvider(_folder.Path, _clock))
        {
            var logger = provider.CreateLogger("ShowTime.Recording");
            logger.LogInformation("ffmpeg started");
            logger.LogError(new InvalidOperationException("boom"), "ffmpeg failed");
        }

        var text = File.ReadAllText(LogFile("20261016"));
        text.ShouldContain("INFO  ShowTime.Recording: ffmpeg started");
        text.ShouldContain("ERROR ShowTime.Recording: ffmpeg failed");
        text.ShouldContain("System.InvalidOperationException: boom");
    }

    [Fact]
    public void Debug_entries_are_left_out()
    {
        using (var provider = new FileLoggerProvider(_folder.Path, _clock))
            provider.CreateLogger("x").LogDebug("noise");
        File.Exists(LogFile("20261016")).ShouldBeFalse();
    }

    [Fact]
    public void Files_older_than_a_week_are_deleted_at_start()
    {
        File.WriteAllText(LogFile("20261001"), "old");
        File.WriteAllText(LogFile("20261012"), "recent");
        using (new FileLoggerProvider(_folder.Path, _clock))
        {
        }
        File.Exists(LogFile("20261001")).ShouldBeFalse();
        File.Exists(LogFile("20261012")).ShouldBeTrue();
    }
}
