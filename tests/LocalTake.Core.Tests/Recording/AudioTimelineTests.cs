using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class AudioTimelineTests
{
    private static readonly RawAudioFormat Format = new("f32le", 48000, 2);
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);

    /// <summary>Frames that are not silence, so that they can be told apart from the padding.</summary>
    private static byte[] Frames(int count) => Enumerable.Repeat((byte)7, count * Format.BlockAlign).ToArray();

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void Audio_at_the_origin_is_written_as_is()
    {
        var timeline = new AudioTimeline(Format, T0);
        var pcm = Frames(480);
        timeline.Place(T0, pcm).ShouldBe(pcm);
        timeline.FramesWritten.ShouldBe(480);
    }

    [Fact]
    public void A_gap_before_audio_is_filled_with_silence()
    {
        var timeline = new AudioTimeline(Format, T0);
        var bytes = timeline.Place(T0 + Ms(10), Frames(480));
        bytes.Length.ShouldBe(960 * 8);
        bytes.AsSpan(0, 480 * 8).ToArray().ShouldAllBe(b => b == 0);
        bytes.AsSpan(480 * 8).ToArray().ShouldAllBe(b => b == 7);
        timeline.FramesWritten.ShouldBe(960);
    }

    [Fact]
    public void Audio_already_covered_by_silence_is_trimmed()
    {
        var timeline = new AudioTimeline(Format, T0);
        timeline.PadTo(T0 + Ms(260)).Length.ShouldBe(480 * 8);
        var bytes = timeline.Place(T0 + Ms(5), Frames(480));
        bytes.Length.ShouldBe(240 * 8);
        timeline.FramesWritten.ShouldBe(720);
    }

    [Fact]
    public void Audio_wholly_covered_is_dropped()
    {
        var timeline = new AudioTimeline(Format, T0);
        timeline.Place(T0, Frames(960));
        timeline.Place(T0 + Ms(5), Frames(240)).ShouldBeEmpty();
        timeline.FramesWritten.ShouldBe(960);
    }

    [Fact]
    public void Audio_before_the_origin_is_dropped()
    {
        var timeline = new AudioTimeline(Format, T0);
        timeline.Place(T0 - Ms(20), Frames(480)).ShouldBeEmpty();
        timeline.Place(T0 - Ms(5), Frames(480)).Length.ShouldBe(240 * 8);
    }

    [Fact]
    public void Idle_time_is_padded_up_to_now_minus_the_slack()
    {
        var timeline = new AudioTimeline(Format, T0);
        timeline.PadTo(T0 + Ms(200)).ShouldBeEmpty();
        var silence = timeline.PadTo(T0 + Ms(1250));
        silence.Length.ShouldBe(48000 * 8);
        silence.ShouldAllBe(b => b == 0);
        timeline.PadTo(T0 + Ms(1250)).ShouldBeEmpty();
    }
}
