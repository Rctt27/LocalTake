using System.IO.Abstractions;
using System.Windows;
using NSubstitute;
using ShowTime.Adapters;
using ShowTime.Core.Editor;
using ShowTime.Core.Files;
using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;
using ShowTime.Core.Settings;

namespace ShowTime.Windows.Tests.Editor;

public sealed class EditorExportTests
{
    internal static EditorWindow OpenEditor(System.Windows.Media.Imaging.BitmapSource image)
    {
        var settings = new AppSettings(SettingsDefaults.ForWindows(@"C:\Captures"));
        var store = Substitute.For<ISettingsStore>();
        var clipboard = Substitute.For<IClipboard>();
        var fileSystem = new FileSystem();
        var saver = new ImageSaver(settings, store, new OutputPathAllocator(fileSystem, TimeProvider.System), new FileCounter(settings, store),
            Substitute.For<IImageCodec>(), clipboard, fileSystem);
        var editor = new EditorWindow(image, new EditorContext(settings, store, saver, clipboard, Substitute.For<IShell>()))
        {
            // Off screen and in the background: the tests must not disturb whoever uses the computer.
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        editor.Show();
        Sta.DoEvents();
        return editor;
    }

    [Fact]
    public async Task Export_keeps_the_capture_size_and_pixels_at_any_zoom()
    {
        var (width, height, pixels, expected) = await Sta.Run(() =>
        {
            var source = TestImages.Gradient(301, 203);
            var editor = OpenEditor(source);
            editor.SetZoom(2.0);
            Sta.DoEvents();
            var exported = editor.RenderImage();
            return (exported.PixelWidth, exported.PixelHeight, TestImages.Pixels(exported), TestImages.Pixels(source));
        });

        width.ShouldBe(301);
        height.ShouldBe(203);
        pixels.ShouldBe(expected);
    }

    [Fact]
    public async Task Blur_matches_the_core_pixelation()
    {
        var area = new PixelRect(10, 20, 90, 50);
        var (exported, source, dpiScale) = await Sta.Run(() =>
        {
            var image = TestImages.Gradient(200, 120);
            var editor = OpenEditor(image);
            editor.AddBlur(area);
            Sta.DoEvents();
            return (WpfImage.ToPixels(editor.RenderImage()), WpfImage.ToPixels(image), editor.DpiScale);
        });

        var (columns, rows) = EditorGeometry.PixelationGrid(area, EditorGeometry.PixelationBlock(area, dpiScale));
        exported.Crop(area).Pixels.ShouldBe(source.Pixelate(area, columns, rows).Pixels);
        var outside = new PixelRect(0, 0, 200, 20);
        exported.Crop(outside).Pixels.ShouldBe(source.Crop(outside).Pixels);
    }
}
