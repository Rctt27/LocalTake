using System.Diagnostics;
using System.Security;
using LocalTake.Core.Ports;

namespace LocalTake.Platform.MacOS;

/// <summary>NSWindow settings for LocalTake's own windows.</summary>
public sealed class MacNativeWindows : INativeWindows
{
    /// <summary>NSWindowSharingNone: the window is left out of screenshots and screen recordings.</summary>
    public bool ExcludeFromCapture(nint nativeWindow)
    {
        if (nativeWindow == 0)
            return false;
        ObjC.Send(nativeWindow, ObjC.Sel("setSharingType:"), 0L);
        return true;
    }

    /// <summary>The screen saver level is above the menu bar and the Dock, where the selection overlay must be.</summary>
    public void RaiseAboveEverything(nint nativeWindow)
    {
        const long screenSaverWindowLevel = 1000;
        if (nativeWindow != 0)
            ObjC.Send(nativeWindow, ObjC.Sel("setLevel:"), screenSaverWindowLevel);
    }
}

/// <summary>The general pasteboard: images as PNG, text as UTF-8.</summary>
public sealed class MacClipboard : INativeClipboard
{
    private const string PngType = "public.png";
    private const string TiffType = "public.tiff";
    private const string TextType = "public.utf8-plain-text";

    public void SetImage(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var pasteboard = General();
        ObjC.Send(pasteboard, ObjC.Sel("clearContents"));
        ObjC.SendBool(pasteboard, ObjC.Sel("setData:forType:"), ObjC.Data(MacImages.EncodePng(image)), ObjC.String(PngType));
    }

    public void SetText(string text)
    {
        var pasteboard = General();
        ObjC.Send(pasteboard, ObjC.Sel("clearContents"));
        ObjC.SendBool(pasteboard, ObjC.Sel("setString:forType:"), ObjC.String(text), ObjC.String(TextType));
    }

    public PixelBuffer? GetImage()
    {
        var pasteboard = General();
        foreach (var type in new[] { PngType, TiffType })
        {
            var data = ObjC.Send(pasteboard, ObjC.Sel("dataForType:"), ObjC.String(type));
            if (data != 0)
                return MacImages.Decode(ObjC.Bytes(data));
        }
        return null;
    }

    private static nint General() => ObjC.Send(ObjC.Class("NSPasteboard"), ObjC.Sel("generalPasteboard"));
}

/// <summary>The Finder, through the "open" command.</summary>
public sealed class MacShell : IShell
{
    public void Reveal(string path) => Open("-R", path);

    public void OpenFolder(string path) => Open(path);

    public void OpenFile(string path) => Open(path);

    private static void Open(params string[] args)
    {
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/open", args) { UseShellExecute = false, CreateNoWindow = true });
    }
}

/// <summary>The Trash, through NSFileManager so that the Finder can put the file back.</summary>
public sealed class MacFileTrash : IFileTrash
{
    public void MoveToTrash(string path)
    {
        var manager = ObjC.Send(ObjC.Class("NSFileManager"), ObjC.Sel("defaultManager"));
        var url = ObjC.Send(ObjC.Class("NSURL"), ObjC.Sel("fileURLWithPath:"), ObjC.String(Path.GetFullPath(path)));
        if (!ObjC.SendBool(manager, ObjC.Sel("trashItemAtURL:resultingItemURL:error:"), url, 0, 0))
            throw new IOException("Le fichier n'a pas pu être placé dans la Corbeille.");
    }
}

/// <summary>A LaunchAgent in ~/Library/LaunchAgents that opens LocalTake at login.</summary>
/// <param name="launchAgentsFolder">Usually ~/Library/LaunchAgents.</param>
/// <param name="executablePath">The running executable; inside LocalTake.app, the bundle is opened instead.</param>
public sealed class MacStartupRegistration(string launchAgentsFolder, string executablePath) : IStartupRegistration
{
    public const string Label = "io.github.rctt27.localtake";

    private string PlistPath => Path.Combine(launchAgentsFolder, Label + ".plist");

    public bool IsEnabled => File.Exists(PlistPath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PlistPath);
            return;
        }
        Directory.CreateDirectory(launchAgentsFolder);
        File.WriteAllText(PlistPath, Plist(ProgramArguments()));
    }

    /// <summary>"open -a LocalTake.app" when running from a bundle, so that macOS applies the bundle's settings.</summary>
    internal string[] ProgramArguments()
    {
        var bundle = BundlePath(executablePath);
        return bundle is not null ? ["/usr/bin/open", "-a", bundle] : [executablePath];
    }

    internal static string? BundlePath(string executable)
    {
        // …/LocalTake.app/Contents/MacOS/LocalTake
        const string marker = ".app/Contents/MacOS/";
        var index = executable.LastIndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? null : executable[..(index + ".app".Length)];
    }

    private static string Plist(IEnumerable<string> arguments) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
        {string.Concat(arguments.Select(a => $"    <string>{SecurityElement.Escape(a)}</string>\n"))}  </array>
          <key>RunAtLoad</key>
          <true/>
        </dict>
        </plist>

        """;
}
