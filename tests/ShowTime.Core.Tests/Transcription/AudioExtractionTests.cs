using ShowTime.Core.Transcription;

namespace ShowTime.Core.Tests.Transcription;

public sealed class AudioExtractionTests
{
    [Fact]
    public void The_first_sound_of_the_video_is_written_as_16_kHz_mono_wav()
    {
        AudioExtraction.Arguments("video.mp4", "sound.wav").ShouldBe(
        [
            "-hide_banner", "-loglevel", "error", "-y", "-i", "video.mp4", "-map", "0:a:0", "-vn",
            "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", "sound.wav"
        ]);
    }

    [Theory]
    [InlineData("Stream map '0:a:0' matches no streams.", true)]
    [InlineData("Output file #0 does not contain any stream", true)]
    [InlineData("video.mp4: No such file or directory", false)]
    public void A_video_without_sound_is_recognised(string error, bool noSound) =>
        AudioExtraction.SaysNoSound(error).ShouldBe(noSound);
}
