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

    /// <summary>
    /// The blur of the editor: the area cut into a grid of blocks, each filled with its average colour. The result
    /// has the size of the area.
    /// </summary>
    public PixelBuffer Pixelate(PixelRect area, int columns, int rows)
    {
        var source = Crop(area);
        columns = Math.Clamp(columns, 1, area.Width);
        rows = Math.Clamp(rows, 1, area.Height);
        var pixels = new byte[source.Pixels.Length];
        Span<long> sum = stackalloc long[4];
        for (var row = 0; row < rows; row++)
        {
            int top = area.Height * row / rows, bottom = area.Height * (row + 1) / rows;
            for (var column = 0; column < columns; column++)
            {
                int left = area.Width * column / columns, right = area.Width * (column + 1) / columns;
                sum.Clear();
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                        for (var c = 0; c < 4; c++)
                            sum[c] += source.Pixels[y * source.Stride + x * 4 + c];
                var count = (bottom - top) * (right - left);
                for (var y = top; y < bottom; y++)
                    for (var x = left; x < right; x++)
                        for (var c = 0; c < 4; c++)
                            pixels[y * source.Stride + x * 4 + c] = (byte)((sum[c] + count / 2) / count);
            }
        }
        return new PixelBuffer(area.Width, area.Height, pixels);
    }

    /// <summary>A copy of an area, which must lie inside the image.</summary>
    public PixelBuffer Crop(PixelRect area)
    {
        if (area.IsEmpty || area.X < 0 || area.Y < 0 || area.Right > Width || area.Bottom > Height)
            throw new ArgumentOutOfRangeException(nameof(area), area, "The area must lie inside the image.");
        var stride = area.Width * 4;
        var pixels = new byte[stride * area.Height];
        for (var y = 0; y < area.Height; y++)
            Array.Copy(Pixels, (area.Y + y) * Stride + area.X * 4, pixels, y * stride, stride);
        return new PixelBuffer(area.Width, area.Height, pixels);
    }
}

/// <summary>The screen as the operating system sees it, in physical pixels.</summary>
public interface IScreenGrabber
{
    /// <summary>Captures an area of the virtual desktop.</summary>
    PixelBuffer Grab(PixelRect area);

    /// <summary>Visible top-level windows, front to back.</summary>
    IReadOnlyList<PixelRect> VisibleWindows();
}

/// <summary>What LocalTake's own windows need from the window manager. Handles are HWNDs or NSWindows.</summary>
public interface INativeWindows
{
    /// <summary>Keeps a window visible on screen but out of screenshots and recordings.</summary>
    /// <returns>False when the system does not support it.</returns>
    bool ExcludeFromCapture(nint nativeWindow);

    /// <summary>Puts the selection overlay above everything, the taskbar or the menu bar and the Dock included.</summary>
    void RaiseAboveEverything(nint nativeWindow);
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
