using LocalTake.Core.Transcription;

namespace LocalTake.Core.Tests.Transcription;

public sealed class TimecodeTests
{
    [Theory]
    [InlineData(0, "00:00.000")]
    [InlineData(62_345, "01:02.345")]
    [InlineData(3_725_042, "1:02:05.042")]
    public void Format_shows_minutes_seconds_and_milliseconds_and_hours_when_needed(int milliseconds, string expected) =>
        Timecode.Format(TimeSpan.FromMilliseconds(milliseconds)).ShouldBe(expected);

    [Fact]
    public void Format_shows_a_negative_time_as_zero() => Timecode.Format(TimeSpan.FromSeconds(-1)).ShouldBe("00:00.000");

    [Theory]
    [InlineData("62.3", 62_300)]
    [InlineData("62,3", 62_300)]
    [InlineData("1:02.3", 62_300)]
    [InlineData(" 01:02.345 ", 62_345)]
    [InlineData("00:01:02,345", 62_345)]
    [InlineData("1:02:05", 3_725_000)]
    [InlineData("7", 7_000)]
    public void TryParse_accepts_the_usual_ways_of_writing_a_time(string text, int expectedMilliseconds)
    {
        Timecode.TryParse(text, out var value).ShouldBeTrue();
        value.ShouldBe(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:60")]
    [InlineData("-3")]
    [InlineData("1:2:3:4")]
    [InlineData("1.2.3")]
    public void TryParse_refuses_anything_else(string text) => Timecode.TryParse(text, out _).ShouldBeFalse();
}
