using ShowTime.Core.Recording;

namespace ShowTime.Core.Tests.Recording;

public sealed class PipedAudioTests
{
    [Theory]
    [InlineData(true, 32, "f32le", 8)]
    [InlineData(false, 16, "s16le", 4)]
    [InlineData(false, 24, "s24le", 6)]
    [InlineData(false, 32, "s32le", 8)]
    public void The_device_format_maps_to_ffmpeg_s_raw_format(bool isFloat, int bits, string sampleFormat, int blockAlign)
    {
        var format = RawAudioFormat.From(isFloat, bits, 48000, 2);
        format.ShouldBe(new RawAudioFormat(sampleFormat, 48000, 2));
        format.BlockAlign.ShouldBe(blockAlign);
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 64)]
    public void Other_formats_are_refused(bool isFloat, int bits) =>
        Should.Throw<NotSupportedException>(() => RawAudioFormat.From(isFloat, bits, 48000, 2));

    [Fact]
    public void Silence_catches_up_with_the_clock() =>
        SilencePadding.FramesDue(TimeSpan.FromSeconds(1), 0, 48000).ShouldBe(48000);

    [Fact]
    public void No_silence_is_due_while_ahead_of_the_clock() =>
        SilencePadding.FramesDue(TimeSpan.FromSeconds(0.5), 48000, 48000).ShouldBe(0);
}
