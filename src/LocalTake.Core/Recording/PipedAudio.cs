namespace LocalTake.Core.Recording;

/// <summary>Raw PCM as ffmpeg reads it: <c>-f SampleFormat -ar SampleRate -ac Channels</c>.</summary>
public sealed record RawAudioFormat(string SampleFormat, int SampleRate, int Channels)
{
    /// <summary>Bytes per frame (one sample of every channel).</summary>
    public int BlockAlign => SampleFormat switch
    {
        "s16le" => 2,
        "s24le" => 3,
        _ => 4
    } * Channels;

    public static RawAudioFormat From(bool isFloat, int bitsPerSample, int sampleRate, int channels)
    {
        var sampleFormat = (isFloat, bitsPerSample) switch
        {
            (true, 32) => "f32le",
            (false, 16) => "s16le",
            (false, 24) => "s24le",
            (false, 32) => "s32le",
            _ => throw new NotSupportedException(
                $"Format audio non pris en charge : {bitsPerSample} bits{(isFloat ? " flottants" : "")}.")
        };
        return new RawAudioFormat(sampleFormat, sampleRate, channels);
    }
}

/// <summary>An audio input that ffmpeg reads from a named pipe.</summary>
public sealed record PipedAudio(string PipePath, RawAudioFormat Format);

public static class SilencePadding
{
    /// <summary>Frames of silence that bring a stream level with the clock; never negative.</summary>
    public static long FramesDue(TimeSpan elapsed, long framesWritten, int sampleRate) =>
        Math.Max(0, (long)Math.Round(elapsed.TotalSeconds * sampleRate) - framesWritten);
}
