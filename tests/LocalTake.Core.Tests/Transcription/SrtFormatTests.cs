using LocalTake.Core.Transcription;

namespace LocalTake.Core.Tests.Transcription;

public sealed class SrtFormatTests
{
    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void Write_numbers_the_cues_and_uses_srt_timecodes()
    {
        var text = SrtFormat.Write(
        [
            new SubtitleCue(Ms(1_500), Ms(4_000), "Bonjour"),
            new SubtitleCue(Ms(3_725_042), Ms(3_726_000), "Deux\nlignes")
        ]);

        text.ShouldBe(
            "1\r\n00:00:01,500 --> 00:00:04,000\r\nBonjour\r\n\r\n" +
            "2\r\n01:02:05,042 --> 01:02:06,000\r\nDeux\r\nlignes\r\n\r\n");
    }

    [Fact]
    public void Parse_reads_back_what_write_wrote()
    {
        SubtitleCue[] cues = [new(Ms(0), Ms(2_000), "Un"), new(Ms(2_500), Ms(3_010), "Deux\nlignes")];

        SrtFormat.Parse(SrtFormat.Write(cues)).ShouldBe(cues);
    }

    [Fact]
    public void Parse_accepts_a_bom_unix_line_endings_dots_and_no_final_blank_line()
    {
        var cues = SrtFormat.Parse("﻿1\n00:00:01.5 --> 00:00:02.25\nUn\n\n\n2\n00:00:03,000 --> 00:00:04,000\nDeux");

        cues.ShouldBe([new SubtitleCue(Ms(1_500), Ms(2_250), "Un"), new SubtitleCue(Ms(3_000), Ms(4_000), "Deux")]);
    }

    [Fact]
    public void Parse_skips_blocks_without_a_time_line()
    {
        var cues = SrtFormat.Parse("n'importe quoi\n\n1\n00:00:01,000 --> 00:00:02,000\nUn\n");

        cues.ShouldBe([new SubtitleCue(Ms(1_000), Ms(2_000), "Un")]);
    }

    [Fact]
    public void Parse_keeps_a_cue_without_text_as_empty()
    {
        SrtFormat.Parse("1\r\n00:00:01,000 --> 00:00:02,000\r\n\r\n").ShouldBe([new SubtitleCue(Ms(1_000), Ms(2_000), "")]);
    }
}
