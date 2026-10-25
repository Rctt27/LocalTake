namespace LocalTake.Core.Recording;

/// <summary>Raw float samples kept one buffer per channel (as ScreenCaptureKit delivers them), made into frames.</summary>
public static class PlanarAudio
{
    /// <param name="planes">One buffer of 32-bit float samples per channel, all of the same length.</param>
    /// <returns>The samples interleaved, frame after frame, as ffmpeg reads f32le.</returns>
    public static byte[] Interleave(IReadOnlyList<byte[]> planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Count == 1)
            return planes[0];
        const int SampleSize = sizeof(float);
        var length = planes[0].Length;
        if (planes.Any(plane => plane.Length != length))
            throw new ArgumentException("Every channel must hold as many samples.", nameof(planes));

        var frames = new byte[length * planes.Count];
        for (var sample = 0; sample < length / SampleSize; sample++)
        {
            for (var channel = 0; channel < planes.Count; channel++)
                planes[channel].AsSpan(sample * SampleSize, SampleSize)
                    .CopyTo(frames.AsSpan((sample * planes.Count + channel) * SampleSize));
        }
        return frames;
    }
}
