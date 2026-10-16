using LocalTake.Core.Geometry;

namespace LocalTake.Core.Ports;

/// <summary>
/// Pixels exchanged with the operating system, 32 bits per pixel in B, G, R, A order, rows top to bottom with no
/// padding. Screen captures are opaque.
/// </summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Expected width × height × 4 bytes.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;

#pragma warning disable CA1819 // The pixel array is handed to the UI framework and the OS without copying.
    public byte[] Pixels { get; }
#pragma warning restore CA1819
}

/// <summary>The screen as the operating system sees it, in physical pixels.</summary>
public interface IScreenGrabber
{
    /// <summary>Captures an area of the virtual desktop.</summary>
    PixelBuffer Grab(PixelRect area);

    /// <summary>Visible top-level windows, front to back.</summary>
    IReadOnlyList<PixelRect> VisibleWindows();
}

/// <summary>Keeps a window visible on screen but out of screenshots and recordings.</summary>
public interface ICaptureExclusion
{
    /// <param name="nativeWindow">The window's native handle (HWND, NSWindow).</param>
    /// <returns>False when the system does not support it.</returns>
    bool Exclude(nint nativeWindow);
}

/// <summary>The file manager.</summary>
public interface IShell
{
    /// <summary>Opens the folder and selects the file in it.</summary>
    void Reveal(string path);

    void OpenFolder(string path);

    /// <summary>Opens the file with its default application.</summary>
    void OpenFile(string path);
}

public interface IFileTrash
{
    /// <summary>Moves the file to the Recycle Bin or the Trash, from where the user can restore it.</summary>
    void MoveToTrash(string path);
}

/// <summary>Starting LocalTake when the user logs in.</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>The system clipboard, for what the UI framework cannot do on every platform (images).</summary>
public interface INativeClipboard
{
    void SetImage(PixelBuffer image);
    void SetText(string text);
    PixelBuffer? GetImage();
}
