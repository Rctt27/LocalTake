using AppKit;
using Foundation;
using LocalTake.Core.Ports;
using LocalTake.Mac.Adapters;
using ObjCRuntime;
using ServiceManagement;

namespace LocalTake.Mac.Platform;

/// <summary>LocalTake's own windows: out of captures, and above the menu bar and the Dock.</summary>
internal sealed class MacNativeWindows : INativeWindows
{
    public bool ExcludeFromCapture(nint nativeWindow)
    {
        Window(nativeWindow).SharingType = NSWindowSharingType.None;
        return true;
    }

    public void RaiseAboveEverything(nint nativeWindow)
    {
        var window = Window(nativeWindow);
        window.Level = NSWindowLevel.ScreenSaver;
        window.CollectionBehavior |= NSWindowCollectionBehavior.CanJoinAllSpaces | NSWindowCollectionBehavior.FullScreenAuxiliary;
    }

    private static NSWindow Window(nint handle) =>
        Runtime.GetNSObject<NSWindow>(handle) ?? throw new ArgumentException("Not a window.", nameof(handle));
}

/// <summary>The pasteboard. Images go as PNG and TIFF, so that every application finds a type it reads.</summary>
internal sealed class MacClipboard : IClipboard
{
    private const string Png = "public.png";
    private const string Tiff = "public.tiff";
    private const string Text = "public.utf8-plain-text";

    public void SetImage(IImage image)
    {
        using var representation = new NSBitmapImageRep(MacImage.Unwrap(image));
        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        if (representation.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary()) is { } png)
            pasteboard.SetDataForType(png, Png);
        if (representation.TiffRepresentation is { } tiff)
            pasteboard.SetDataForType(tiff, Tiff);
    }

    public void SetText(string text)
    {
        var pasteboard = NSPasteboard.GeneralPasteboard;
        pasteboard.ClearContents();
        pasteboard.SetStringForType(text, Text);
    }

}

/// <summary>The Finder.</summary>
internal sealed class MacShell : IShell
{
    public void Reveal(string path)
    {
        using var url = NSUrl.FromFilename(path);
        NSWorkspace.SharedWorkspace.ActivateFileViewer([url]);
    }

    public void OpenFolder(string path) => OpenFile(path);

    public void OpenFile(string path)
    {
        using var url = NSUrl.FromFilename(path);
        NSWorkspace.SharedWorkspace.OpenUrl(url);
    }
}

/// <summary>The Trash, from which the Finder can put a file back.</summary>
internal sealed class MacFileTrash : IFileTrash
{
    public void MoveToTrash(string path)
    {
        using var url = NSUrl.FromFilename(path);
        if (!NSFileManager.DefaultManager.TrashItem(url, out _, out var error))
            throw new IOException("Impossible de mettre le fichier à la Corbeille : " + error?.LocalizedDescription);
    }
}

/// <summary>"Open at login", as System Settings > General > Login Items lists it.</summary>
internal sealed class MacStartupRegistration : IStartupRegistration
{
    public bool IsEnabled => SMAppService.MainApp.Status == SMAppServiceStatus.Enabled;

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled)
            return;
        NSError? error;
        var done = enabled ? SMAppService.MainApp.Register(out error) : SMAppService.MainApp.Unregister(out error);
        if (!done)
            throw new InvalidOperationException("macOS a refusé l'ouverture à la connexion : " + error?.LocalizedDescription);
    }
}
