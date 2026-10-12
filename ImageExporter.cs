using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LocalTake;

internal static class ImageExporter
{
    /// <summary>Saves into the output folder under the configured name pattern; returns the full path.</summary>
    public static string SaveToOutputFolder(BitmapSource image, AppSettings settings)
    {
        Directory.CreateDirectory(settings.OutputFolder);
        var extension = settings.Format == "jpg" ? ".jpg" : ".png";
        var baseName = ExpandPattern(settings.FileNamePattern, settings.Counter, DateTime.Now);

        var path = Path.Combine(settings.OutputFolder, baseName + extension);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(settings.OutputFolder, $"{baseName} ({i}){extension}");

        Save(image, path, settings.JpegQuality);
        ConsumeCounter(settings);
        if (settings.CopyPathAfterSave)
            CopyText(path);
        return path;
    }

    public static string? SaveWithDialog(BitmapSource image, AppSettings settings, Window? owner)
    {
        var suggestedName = ExpandPattern(settings.FileNamePattern, settings.Counter, DateTime.Now);
        var dialog = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",
            FilterIndex = settings.Format == "jpg" ? 2 : 1,
            FileName = suggestedName,
            InitialDirectory = Directory.Exists(settings.LastSaveAsFolder) ? settings.LastSaveAsFolder : settings.OutputFolder
        };
        if (dialog.ShowDialog(owner) != true)
            return null;

        Save(image, dialog.FileName, settings.JpegQuality);
        // The number is used only if the suggested name was kept.
        if (string.Equals(Path.GetFileNameWithoutExtension(dialog.FileName), suggestedName, StringComparison.OrdinalIgnoreCase))
            ConsumeCounter(settings);
        settings.LastSaveAsFolder = Path.GetDirectoryName(dialog.FileName);
        settings.TrySave();
        if (settings.CopyPathAfterSave)
            CopyText(dialog.FileName);
        return dialog.FileName;
    }

    public static void Save(BitmapSource image, string path, int jpegQuality)
    {
        var isJpeg = Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";
        BitmapEncoder encoder = isJpeg
            ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) }
            : new PngBitmapEncoder();
        // JPEG has no alpha channel; the editor renders with one.
        var frame = isJpeg ? new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgr24, null, 0) : image;
        encoder.Frames.Add(BitmapFrame.Create(frame));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// File name for a pattern, without extension. Side-effect free: the settings preview uses it too, and %NUM%
    /// only advances once a file is actually written (see <see cref="ConsumeCounter"/>).
    /// </summary>
    public static string ExpandPattern(string pattern, int counter, DateTime now)
    {
        var name = pattern
            .Replace("%YYYY%", now.ToString("yyyy"))
            .Replace("%MM%", now.ToString("MM"))
            .Replace("%DD%", now.ToString("dd"))
            .Replace("%hh%", now.ToString("HH"))
            .Replace("%mm%", now.ToString("mm"))
            .Replace("%ss%", now.ToString("ss"))
            .Replace("%NUM%", counter.ToString("D6"));

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Capture" : name;
    }

    private static void ConsumeCounter(AppSettings settings)
    {
        if (!settings.FileNamePattern.Contains("%NUM%"))
            return;
        settings.Counter++;
        settings.TrySave();
    }

    public static void CopyImage(BitmapSource image) => WithClipboardRetry(() => Clipboard.SetImage(image));

    public static void CopyText(string text) => WithClipboardRetry(() => Clipboard.SetText(text));

    /// <summary>Another process can briefly hold the clipboard open.</summary>
    private static void WithClipboardRetry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5)
            {
                Thread.Sleep(100);
            }
        }
    }
}
