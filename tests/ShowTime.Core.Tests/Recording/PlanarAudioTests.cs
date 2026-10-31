using System.Runtime.InteropServices;
using ShowTime.Core.Recording;

namespace ShowTime.Core.Tests.Recording;

public sealed class PlanarAudioTests
{
    private static byte[] Floats(params float[] samples) => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

    [Fact]
    public void Two_planes_are_interleaved() =>
        PlanarAudio.Interleave([Floats(1f, 2f), Floats(-1f, -2f)]).ShouldBe(Floats(1f, -1f, 2f, -2f));

    [Fact]
    public void One_plane_is_kept_as_it_is() =>
        PlanarAudio.Interleave([Floats(1f, 2f)]).ShouldBe(Floats(1f, 2f));

    [Fact]
    public void Planes_of_different_lengths_are_refused() =>
        Should.Throw<ArgumentException>(() => PlanarAudio.Interleave([Floats(1f, 2f), Floats(1f)]));
}
