using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class PlayerSeekTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void The_step_is_five_seconds() => PlayerSeek.Step.ShouldBe(S(5));

    [Fact]
    public void Back_goes_five_seconds_back() => PlayerSeek.Back(S(12)).ShouldBe(S(7));

    [Fact]
    public void Back_stops_at_the_start() => PlayerSeek.Back(S(3)).ShouldBe(TimeSpan.Zero);

    [Fact]
    public void Forward_goes_five_seconds_on() => PlayerSeek.Forward(S(12), S(60)).ShouldBe(S(17));

    [Fact]
    public void Forward_stops_at_the_end() => PlayerSeek.Forward(S(58), S(60)).ShouldBe(S(60));

    /// <summary>An unknown duration (zero, before the player knows it) does not hold the position back.</summary>
    [Fact]
    public void Forward_without_a_duration_still_moves() => PlayerSeek.Forward(S(12), TimeSpan.Zero).ShouldBe(S(17));
}
