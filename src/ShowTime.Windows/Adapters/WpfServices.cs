using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ShowTime.Core.Ports;

namespace ShowTime.Adapters;

/// <summary>PNG or JPEG through WPF's encoders.</summary>
internal sealed class WpfImageCodec : IImageCodec
{
    public void Save(IImage image, string path, int jpegQuality)
    {
        var source = WpfImage.Unwrap(image);
        var isJpeg = Path.GetExtension(path).ToUpperInvariant() is ".JPG" or ".JPEG";
        BitmapEncoder encoder = isJpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) }
            : new PngBitmapEncoder();
        // JPEG has no alpha channel; the editor renders with one.
        var frame = isJpeg ? new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0) : source;
        encoder.Frames.Add(BitmapFrame.Create(frame));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

internal sealed class WpfClipboard : IClipboard
{
    public void SetImage(IImage image) => WithRetry(() => Clipboard.SetImage(WpfImage.Unwrap(image)));

    public void SetText(string text) => WithRetry(() => Clipboard.SetText(text));

    /// <summary>Another process can briefly hold the clipboard open.</summary>
    private static void WithRetry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(100);
            }
        }
    }
}

internal sealed class WpfSaveFileDialog(Window? owner) : ISaveFileDialog
{
    public Task<string?> AskAsync(string suggestedName, bool jpeg, string initialDirectory) =>
        Task.FromResult(Ask(suggestedName, jpeg, initialDirectory));

    private string? Ask(string suggestedName, bool jpeg, string initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",
            FilterIndex = jpeg ? 2 : 1,
            FileName = suggestedName,
            InitialDirectory = initialDirectory
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}

internal sealed class WpfDialogs : IUserDialogs
{
    private const string Title = "ShowTime";

    public void ShowError(string message) => MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowWarning(string message) => MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message) => MessageBox.Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public Task<DialogAnswer> AskYesNoCancelAsync(string message) => Task.FromResult(
        MessageBox.Show(message, Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => DialogAnswer.Yes,
            MessageBoxResult.No => DialogAnswer.No,
            _ => DialogAnswer.Cancel
        });
}

internal sealed class DispatcherUiThread(Dispatcher dispatcher) : IUiThread
{
    public void Post(Action action) => dispatcher.BeginInvoke(action);
}
