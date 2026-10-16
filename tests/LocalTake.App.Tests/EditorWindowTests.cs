using System.IO.Abstractions.TestingHelpers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using LocalTake.App.Editor;
using LocalTake.Core.Files;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;
using NSubstitute;

namespace LocalTake.App.Tests;

public sealed class EditorWindowTests
{
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(Path.Combine(Path.GetTempPath(), "LocalTakeEditorUi")));
    private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
    private readonly IClipboard _clipboard = Substitute.For<IClipboard>();
    private readonly IImageCodec _codec = Substitute.For<IImageCodec>();

    /// <summary>A 200×100 image whose blue channel follows x, so that a blur visibly changes it.</summary>
    private static PixelBuffer Gradient()
    {
        var pixels = new byte[200 * 100 * 4];
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 200; x++)
            {
                var i = (y * 200 + x) * 4;
                pixels[i] = (byte)x;
                pixels[i + 1] = 128;
                pixels[i + 2] = 128;
                pixels[i + 3] = 255;
            }
        return new PixelBuffer(200, 100, pixels);
    }

    private EditorWindow Open(PixelBuffer image)
    {
        var fs = new MockFileSystem();
        var counter = new FileCounter(_settings, _store);
        var saver = new ImageSaver(_settings, _store, new OutputPathAllocator(fs, TimeProvider.System), counter, _codec, _clipboard, fs);
        var context = new EditorContext(_settings, _store, saver, _clipboard, Substitute.For<IShell>(), Substitute.For<IUserDialogs>());
        var window = new EditorWindow(image, context);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>A point of the image in window coordinates, whatever the zoom and scroll.</summary>
    private static Point At(EditorWindow window, double x, double y) =>
        window.FindControl<Canvas>("Surface")!.TranslatePoint(new Point(x, y), window)!.Value;

    private static void Drag(EditorWindow window, (double X, double Y) from, (double X, double Y) to)
    {
        window.MouseDown(At(window, from.X, from.Y), MouseButton.Left);
        window.MouseMove(At(window, (from.X + to.X) / 2, (from.Y + to.Y) / 2));
        window.MouseMove(At(window, to.X, to.Y));
        window.MouseUp(At(window, to.X, to.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(EditorWindow window, Key key, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static (byte B, byte G, byte R) Pixel(PixelBuffer image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
    }

    [Fact]
    public async Task Export_is_the_image_at_its_own_size_whatever_the_zoom() => await Ui.Run(() =>
    {
        var window = Open(Gradient());
        Press(window, Key.Add, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        var image = window.RenderImage();

        image.Width.ShouldBe(200);
        image.Height.ShouldBe(100);
        Pixel(image, 150, 50).ShouldBe(((byte)150, (byte)128, (byte)128));
        window.Close();
    });

    [Fact]
    public async Task A_rectangle_is_drawn_in_the_chosen_colour_and_leaves_the_inside_untouched() => await Ui.Run(() =>
    {
        var window = Open(Gradient());
        Press(window, Key.R, RawInputModifiers.None);
        Drag(window, (20, 20), (120, 70));

        var image = window.RenderImage();

        var edge = Pixel(image, 21, 45);
        edge.R.ShouldBeGreaterThan((byte)180);
        edge.G.ShouldBeLessThan((byte)110);
        Pixel(image, 70, 45).ShouldBe(((byte)70, (byte)128, (byte)128));
        window.Close();
    });

    [Fact]
    public async Task Blur_replaces_the_area_with_uniform_blocks() => await Ui.Run(() =>
    {
        var window = Open(Gradient());
        Press(window, Key.B, RawInputModifiers.None);
        Drag(window, (0, 0), (80, 80));

        var image = window.RenderImage();

        // Blocks of 10 pixels (an eighth of 80): the first column of blocks is uniform, unlike the gradient.
        Pixel(image, 1, 1).ShouldBe(Pixel(image, 8, 8));
        Pixel(image, 1, 1).B.ShouldBe((byte)5);
        Pixel(image, 150, 50).B.ShouldBe((byte)150);
        window.Close();
    });

    [Fact]
    public async Task Crop_keeps_the_area_and_undo_brings_the_whole_image_back() => await Ui.Run(() =>
    {
        var window = Open(Gradient());
        Press(window, Key.C, RawInputModifiers.None);
        Drag(window, (10, 10), (60, 50));

        var cropped = window.RenderImage();
        cropped.Width.ShouldBe(50);
        cropped.Height.ShouldBe(40);
        Pixel(cropped, 0, 0).B.ShouldBe((byte)10);

        Press(window, Key.Z, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        window.RenderImage().Width.ShouldBe(200);
        window.Close();
    });

    [Fact]
    public async Task Copy_puts_the_annotated_image_in_the_clipboard() => await Ui.Run(() =>
    {
        var window = Open(Gradient());
        Press(window, Key.C, RawInputModifiers.Control);
        _clipboard.Received(1).SetImage(Arg.Is<IImage>(i => i.Width == 200 && i.Height == 100));
        window.Close();
    });

    [Fact]
    public async Task Save_writes_through_the_codec_and_closing_keeps_the_preferences() => await Ui.Run(() =>
    {
        var window = Open(Gradient());
        Press(window, Key.S, RawInputModifiers.Control);
        _codec.Received(1).Save(Arg.Is<IImage>(i => i.Width == 200), Arg.Any<string>(), 90);

        window.Close();
        Dispatcher.UIThread.RunJobs();
        _store.Received().TrySave(_settings);
    });
}
