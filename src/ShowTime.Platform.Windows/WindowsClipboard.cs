using System.ComponentModel;
using System.Runtime.InteropServices;
using ShowTime.Core.Ports;

namespace ShowTime.Platform.Windows;

/// <summary>The Win32 clipboard: images as 32-bit DIBs, text as UTF-16.</summary>
public sealed unsafe class WindowsClipboard : INativeClipboard
{
    public void SetImage(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var headerSize = sizeof(Win32.BITMAPINFOHEADER);
        var memory = Allocate(headerSize + image.Pixels.Length, data =>
        {
            // Bottom-up rows: some applications ignore a negative height.
            *(Win32.BITMAPINFOHEADER*)data = new Win32.BITMAPINFOHEADER
            {
                biSize = (uint)headerSize,
                biWidth = image.Width,
                biHeight = image.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32.BI_RGB,
                biSizeImage = (uint)image.Pixels.Length
            };
            var pixels = new Span<byte>((byte*)data + headerSize, image.Pixels.Length);
            for (var y = 0; y < image.Height; y++)
                image.Pixels.AsSpan(y * image.Stride, image.Stride).CopyTo(pixels.Slice((image.Height - 1 - y) * image.Stride, image.Stride));
        });
        Put(Win32.CF_DIB, memory);
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = (text.Length + 1) * 2;
        var memory = Allocate(bytes, data =>
        {
            text.AsSpan().CopyTo(new Span<char>((char*)data, text.Length));
            ((char*)data)[text.Length] = '\0';
        });
        Put(Win32.CF_UNICODETEXT, memory);
    }

    public PixelBuffer? GetImage()
    {
        Open();
        try
        {
            if (!Win32.IsClipboardFormatAvailable(Win32.CF_DIB))
                return null;
            var handle = Win32.GetClipboardData(Win32.CF_DIB);
            if (handle == 0)
                return null;
            var data = (byte*)Win32.GlobalLock(handle);
            try
            {
                return data == null ? null : ReadDib(data, (long)Win32.GlobalSize(handle));
            }
            finally
            {
                Win32.GlobalUnlock(handle);
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// <summary>A 24 or 32-bit DIB, top-down or bottom-up, with or without colour masks.</summary>
    private static PixelBuffer? ReadDib(byte* data, long size)
    {
        var header = *(Win32.BITMAPINFOHEADER*)data;
        if (header.biBitCount is not (24 or 32) || header.biCompression is not (Win32.BI_RGB or Win32.BI_BITFIELDS))
            return null;
        var width = header.biWidth;
        var height = Math.Abs(header.biHeight);
        if (width <= 0 || height <= 0)
            return null;
        var masks = header.biCompression == Win32.BI_BITFIELDS && header.biSize == 40 ? 12 : 0;
        var offset = (int)header.biSize + masks;
        var sourceStride = (width * header.biBitCount / 8 + 3) & ~3;
        if (offset + (long)sourceStride * height > size)
            return null;

        var pixels = new byte[width * height * 4];
        var bottomUp = header.biHeight > 0;
        // A 32-bit DIB from most applications leaves alpha at 0: keep the image opaque in that case.
        var anyAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var source = data + offset + (bottomUp ? height - 1 - y : y) * sourceStride;
            for (var x = 0; x < width; x++)
            {
                var target = (y * width + x) * 4;
                var bytesPerPixel = header.biBitCount / 8;
                pixels[target] = source[x * bytesPerPixel];
                pixels[target + 1] = source[x * bytesPerPixel + 1];
                pixels[target + 2] = source[x * bytesPerPixel + 2];
                pixels[target + 3] = bytesPerPixel == 4 ? source[x * 4 + 3] : (byte)255;
                anyAlpha |= pixels[target + 3] != 0;
            }
        }
        if (!anyAlpha)
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
        return new PixelBuffer(width, height, pixels);
    }

    private static nint Allocate(int size, Action<nint> fill)
    {
        var memory = Win32.GlobalAlloc(Win32.GMEM_MOVEABLE, (nuint)size);
        if (memory == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        var data = (nint)Win32.GlobalLock(memory);
        try
        {
            fill(data);
        }
        finally
        {
            Win32.GlobalUnlock(memory);
        }
        return memory;
    }

    /// <summary>Hands the memory over to the clipboard, which then owns it.</summary>
    private static void Put(uint format, nint memory)
    {
        try
        {
            Open();
        }
        catch
        {
            Win32.GlobalFree(memory);
            throw;
        }
        try
        {
            Win32.EmptyClipboard();
            if (Win32.SetClipboardData(format, memory) == 0)
            {
                Win32.GlobalFree(memory);
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
    }

    /// <summary>Another process can briefly hold the clipboard open.</summary>
    private static void Open()
    {
        for (var attempt = 0; ; attempt++)
        {
            if (Win32.OpenClipboard(0))
                return;
            if (attempt >= 10)
                throw new IOException("Le presse-papier est occupé par une autre application.");
            Thread.Sleep(50);
        }
    }
}
