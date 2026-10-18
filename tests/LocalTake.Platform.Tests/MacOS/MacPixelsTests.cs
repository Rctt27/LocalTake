// Pure pixel arithmetic: it runs on every OS, although the assembly is marked for macOS.
#pragma warning disable CA1416

using LocalTake.Platform.MacOS;

namespace LocalTake.Platform.Tests.MacOS;

public sealed class MacPixelsTests
{
    [Fact]
    public void Premultiplied_pixels_get_their_colour_back()
    {
        // 50 % opaque red, premultiplied: R 200 became 100.
        byte[] pixels = [0, 0, 100, 128, 0, 0, 0, 0, 30, 60, 90, 255];

        MacPixels.Unpremultiply(pixels);

        pixels.ShouldBe(new byte[] { 0, 0, 199, 128, 0, 0, 0, 0, 30, 60, 90, 255 });
    }
}
