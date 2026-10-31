
using System.Runtime.InteropServices;
using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;

namespace ShowTime.Platform.Windows;

/// <summary>GDI capture of the virtual desktop, in physical pixels (the process is Per-Monitor-V2 aware).</summary>
public sealed unsafe class WindowsScreenGrabber : IScreenGrabber
{
    public PixelBuffer Grab(PixelRect area)
    {
        var screen = Win32.GetDC(0);
        var memory = Win32.CreateCompatibleDC(screen);
        // A negative height asks for top-down rows, as PixelBuffer holds them.
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Win32.BITMAPINFOHEADER),
            biWidth = area.Width,
            biHeight = -area.Height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Win32.BI_RGB
        };
        var bitmap = Win32.CreateDIBSection(screen, &header, Win32.DIB_RGB_COLORS, out var bits, 0, 0);
        try
        {
            if (bitmap == 0)
                throw new InvalidOperationException("Cannot allocate the capture bitmap.");
            var previous = Win32.SelectObject(memory, bitmap);
            // CAPTUREBLT includes layered windows, as the screen shows them.
            if (!Win32.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, Win32.SRCCOPY | Win32.CAPTUREBLT))
                throw new InvalidOperationException("Screen capture failed: error " + Marshal.GetLastPInvokeError());
            Win32.SelectObject(memory, previous);

            var pixels = new byte[area.Width * area.Height * 4];
            new ReadOnlySpan<byte>(bits, pixels.Length).CopyTo(pixels);
            // GDI leaves the alpha byte undefined; a capture is opaque.
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return new PixelBuffer(area.Width, area.Height, pixels);
        }
        finally
        {
            if (bitmap != 0)
                Win32.DeleteObject(bitmap);
            Win32.DeleteDC(memory);
            _ = Win32.ReleaseDC(0, screen);
        }
    }

    public IReadOnlyList<PixelRect> VisibleWindows()
    {
        var windows = new List<PixelRect>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            Win32.EnumWindows(&OnWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hWnd, nint lParam)
    {
        var windows = (List<PixelRect>)GCHandle.FromIntPtr(lParam).Target!;
        if (Bounds(hWnd) is { } bounds)
            windows.Add(bounds);
        return 1;
    }

    private static PixelRect? Bounds(nint hWnd)
    {
        if (!Win32.IsWindowVisible(hWnd) || Win32.IsIconic(hWnd))
            return null;
        if ((Win32.GetWindowLong(hWnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TRANSPARENT) != 0)
            return null;
        int cloaked;
        if (Win32.DwmGetWindowAttribute(hWnd, Win32.DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0)
            return null;

        // Untitled windows are mostly invisible helpers; the shell ones are kept so the desktop and taskbar stay selectable.
        if (Win32.GetWindowTextLength(hWnd) == 0)
        {
            var buffer = stackalloc char[256];
            var length = Win32.GetClassName(hWnd, buffer, 256);
            var name = new string(buffer, 0, Math.Max(length, 0));
            if (name is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW"))
                return null;
        }

        // The DWM frame excludes the invisible resize borders that GetWindowRect includes.
        Win32.RECT r;
        if (Win32.DwmGetWindowAttribute(hWnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(Win32.RECT)) != 0)
            Win32.GetWindowRect(hWnd, out r);
        var bounds = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return bounds.Width > 1 && bounds.Height > 1 ? bounds : null;
    }
}

/// <summary>Window styles: WDA_EXCLUDEFROMCAPTURE keeps a window visible but out of screenshots and of gdigrab.</summary>
public sealed class WindowsNativeWindows : INativeWindows
{
    public bool ExcludeFromCapture(nint nativeWindow) => Win32.SetWindowDisplayAffinity(nativeWindow, Win32.WDA_EXCLUDEFROMCAPTURE);

    /// <summary>A topmost window already covers the taskbar on Windows.</summary>
    public void RaiseAboveEverything(nint nativeWindow)
    {
    }
}
