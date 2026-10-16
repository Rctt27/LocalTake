using LocalTake.App.Adapters;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using SkiaSharp;

namespace LocalTake.App.Tests;

public sealed class AdapterTests
{
    [Fact]
    public void Every_key_code_has_an_Avalonia_key()
    {
        var mapped = Enum.GetValues<Avalonia.Input.Key>().Select(AvaloniaKeys.ToKeyCode).Where(k => k != KeyCode.None).ToHashSet();
        Enum.GetValues<KeyCode>().Where(k => k != KeyCode.None).ShouldAllBe(k => mapped.Contains(k));
    }

    [Fact]
    public void Modifiers_follow_Avalonia_s()
    {
        AvaloniaKeys.ToModifiers(Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Meta)
            .ShouldBe(HotkeyModifiers.Control | HotkeyModifiers.Meta);
        AvaloniaKeys.IsModifier(Avalonia.Input.Key.LeftShift).ShouldBeTrue();
    }

    [Fact]
    public void Png_keeps_every_pixel()
    {
        var pixels = new byte[3 * 2 * 4];
        for (var i = 0; i < 6; i++)
        {
            pixels[i * 4] = (byte)(40 * i);
            pixels[i * 4 + 1] = 7;
            pixels[i * 4 + 2] = 200;
            pixels[i * 4 + 3] = 255;
        }
        using var stream = new MemoryStream();
        SkiaImageCodec.Encode(new PixelBuffer(3, 2, pixels), stream, jpeg: false, jpegQuality: 90);

        using var decoded = SKBitmap.Decode(stream.ToArray());
        decoded.Width.ShouldBe(3);
        decoded.GetPixel(2, 1).ShouldBe(new SKColor(200, 7, 200));
    }

    [Fact]
    public void Jpeg_is_written_at_the_image_size()
    {
        using var stream = new MemoryStream();
        SkiaImageCodec.Encode(new PixelBuffer(16, 8, Enumerable.Repeat((byte)255, 16 * 8 * 4).ToArray()), stream, jpeg: true, jpegQuality: 80);
        stream.ToArray().Take(2).ShouldBe(new byte[] { 0xFF, 0xD8 });
        using var decoded = SKBitmap.Decode(stream.ToArray());
        decoded.Width.ShouldBe(16);
        decoded.Height.ShouldBe(8);
    }

    [Fact]
    public void A_saved_file_loads_back()
    {
        var file = Path.Combine(Path.GetTempPath(), "LocalTake-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var image = new ImageData(new PixelBuffer(4, 4, Enumerable.Repeat((byte)128, 64).ToArray()));
            new SkiaImageCodec().Save(image, file, 90);
            var loaded = SkiaImageCodec.Load(file)!;
            loaded.Width.ShouldBe(4);
            loaded.Pixels[0].ShouldBe((byte)128);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_file_that_is_not_an_image_loads_as_nothing()
    {
        var file = Path.GetTempFileName();
        try
        {
            SkiaImageCodec.Load(file).ShouldBeNull();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
