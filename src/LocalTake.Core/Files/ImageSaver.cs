using System.IO.Abstractions;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;

namespace LocalTake.Core.Files;

/// <summary>Saves captures to the output folder or through "Save as".</summary>
public sealed class ImageSaver(
    AppSettings settings, ISettingsStore store, OutputPathAllocator paths, FileCounter counter, IImageCodec codec, IClipboard clipboard,
    IFileSystem fileSystem)
{
    /// <summary>Saves into the output folder under the configured name pattern.</summary>
    /// <returns>The full path written.</returns>
    public string SaveToOutputFolder(IImage image)
    {
        var path = paths.Next(settings.OutputFolder, settings.FileNamePattern, settings.Counter, settings.ImageExtension);
        codec.Save(image, path, settings.JpegQuality);
        counter.Consume();
        if (settings.CopyPathAfterSave)
            clipboard.SetText(path);
        return path;
    }

    /// <returns>The path written, or null when the dialog was cancelled.</returns>
    public async Task<string?> SaveAsAsync(IImage image, ISaveFileDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var suggestedName = paths.SuggestedName(settings.FileNamePattern, settings.Counter);
        var initialDirectory = settings.LastSaveAsFolder is { } last && fileSystem.Directory.Exists(last) ? last : settings.OutputFolder;
        if (await dialog.AskAsync(suggestedName, settings.Format == "jpg", initialDirectory) is not { } path)
            return null;

        codec.Save(image, path, settings.JpegQuality);
        // The number is used only if the suggested name was kept.
        if (string.Equals(fileSystem.Path.GetFileNameWithoutExtension(path), suggestedName, StringComparison.OrdinalIgnoreCase))
            counter.Consume();
        settings.LastSaveAsFolder = fileSystem.Path.GetDirectoryName(path);
        store.TrySave(settings);
        if (settings.CopyPathAfterSave)
            clipboard.SetText(path);
        return path;
    }
}
