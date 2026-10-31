namespace ShowTime.Core.Recording;

/// <summary>
/// Turns sound captured in stamped packets into the continuous raw stream ffmpeg reads from a pipe. ffmpeg stamps raw
/// PCM by counting frames, so every gap must be filled: a packet goes where its time puts it, after silence if the
/// stream is behind, trimmed of what silence already covered if it is ahead. A source may also deliver nothing while
/// nothing plays (ScreenCaptureKit): <see cref="PadTo"/> then keeps the stream level with the clock. Small
/// differences between a packet's time and the stream's position (jitter, the audio device's clock drifting from the
/// host's) are not corrected: each correction would click.
/// </summary>
/// <param name="origin">When the stream starts, on the source's clock: the moment ffmpeg opened the pipe.</param>
public sealed class AudioTimeline(RawAudioFormat format, TimeSpan origin)
{
    /// <summary>
    /// How far behind the clock the stream may fall before silence is written: packets arrive some tens of
    /// milliseconds after their time, and silence written ahead of them would cut them.
    /// </summary>
    public static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(250);

    /// <summary>Within this of the stream's position, a packet follows the previous one back to back.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// Further than this from the stream's position, a packet's time is not believed (another time base, a bogus stamp):
    /// it is appended rather than placed after seconds of silence, or dropped as already covered.
    /// </summary>
    public static readonly TimeSpan MaxJump = TimeSpan.FromSeconds(2);

    public long FramesWritten { get; private set; }

    /// <summary>How many packets were appended because their time was not believed: worth logging.</summary>
    public int Reanchors { get; private set; }

    /// <summary>The bytes to write for <paramref name="pcm"/>, captured at <paramref name="at"/>.</summary>
    public byte[] Place(TimeSpan at, ReadOnlySpan<byte> pcm)
    {
        var start = FrameAt(at);
        var offset = Math.Abs(start - FramesWritten);
        if (offset > FramesIn(MaxJump))
        {
            Reanchors++;
            start = FramesWritten;
        }
        else if (offset <= FramesIn(Tolerance))
        {
            start = FramesWritten;
        }
        var frames = pcm.Length / format.BlockAlign;
        var skipped = Math.Clamp(FramesWritten - start, 0, frames);
        var gap = Math.Max(0, start - FramesWritten);
        var kept = pcm[(int)(skipped * format.BlockAlign)..(frames * format.BlockAlign)];
        if (kept.IsEmpty)
            return [];

        var bytes = new byte[gap * format.BlockAlign + kept.Length];
        kept.CopyTo(bytes.AsSpan((int)(gap * format.BlockAlign)));
        FramesWritten += gap + kept.Length / format.BlockAlign;
        return bytes;
    }

    /// <summary>Silence up to <see cref="Slack"/> before <paramref name="now"/>, when no sound came for that long.</summary>
    public byte[] PadTo(TimeSpan now)
    {
        var due = FrameAt(now - Slack) - FramesWritten;
        if (due <= 0)
            return [];
        FramesWritten += due;
        return new byte[due * format.BlockAlign];
    }

    private long FrameAt(TimeSpan time) => FramesIn(time - origin);

    private long FramesIn(TimeSpan duration) => (long)Math.Round(duration.TotalSeconds * format.SampleRate);
}
