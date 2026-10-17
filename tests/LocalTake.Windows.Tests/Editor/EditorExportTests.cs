using System.IO.Abstractions;
using System.Windows;
using LocalTake.Core.Files;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;
using NSubstitute;

namespace LocalTake.Windows.Tests.Editor;

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
        var editor = new EditorWindow(image, new EditorContext(settings, store, saver, clipboard))
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
}
