using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using LocalTake.App.Editor;
using LocalTake.Core.Ports;

namespace LocalTake.App.Adapters;

internal static class AppIcon
{
    private static readonly Lazy<WindowIcon> Icon = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://LocalTake/Assets/LocalTake.ico"));
        return new WindowIcon(stream);
    });

    public static WindowIcon Window => Icon.Value;
}

/// <summary>"Save as" through the system dialog.</summary>
internal sealed class StorageSaveDialog(TopLevel owner) : ISaveFileDialog
{
    private static readonly FilePickerFileType Png = new("PNG") { Patterns = ["*.png"], AppleUniformTypeIdentifiers = ["public.png"] };
    private static readonly FilePickerFileType Jpeg = new("JPEG") { Patterns = ["*.jpg", "*.jpeg"], AppleUniformTypeIdentifiers = ["public.jpeg"] };

    public async Task<string?> AskAsync(string suggestedName, bool jpeg, string initialDirectory)
    {
        var storage = owner.StorageProvider;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = jpeg ? "jpg" : "png",
            FileTypeChoices = jpeg ? [Jpeg, Png] : [Png, Jpeg],
            SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(initialDirectory),
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }
}

/// <summary>Opens each capture in its own editor window.</summary>
internal sealed class EditorLauncher(EditorContext context) : IEditorLauncher
{
    public void Open(IImage image)
    {
        var editor = new EditorWindow(ImageData.Of(image).Pixels, context);
        editor.Closed += (_, _) => image.Dispose();
        editor.Show();
        editor.Activate();
    }
}
