using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using WpfPixelFormats = System.Windows.Media.PixelFormats;

namespace LocalTake;

internal static class ScreenCapture
{
    /// <summary>All monitors, in physical pixels; the origin can be negative.</summary>
    public static Rectangle VirtualScreen => Forms.SystemInformation.VirtualScreen;

    public static Bitmap Capture(Rectangle area)
    {
        var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    /// <summary>Converts at 96 DPI so that one WPF unit equals one screen pixel in the editor.</summary>
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);
        try
        {
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, WpfPixelFormats.Bgr32, null,
                data.Scan0, data.Stride * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>Re-encodes any image (file, clipboard) at 96 DPI, for the same reason as <see cref="ToBitmapSource"/>.</summary>
    public static BitmapSource Normalize(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, WpfPixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
            WpfPixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>Visible top-level windows, front to back, in physical screen coordinates.</summary>
    public static List<Rectangle> VisibleWindows()
    {
        var windows = new List<Rectangle>();
        var className = new StringBuilder(256);
        Native.EnumWindows((hWnd, _) =>
        {
            if (!Native.IsWindowVisible(hWnd) || Native.IsIconic(hWnd))
                return true;
            if ((Native.GetWindowLong(hWnd, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0)
                return true;
            if (Native.DwmGetWindowAttribute(hWnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            // Untitled windows are mostly invisible helpers; the shell ones are kept so the desktop and taskbar stay selectable.
            if (Native.GetWindowTextLength(hWnd) == 0)
            {
                className.Clear();
                Native.GetClassName(hWnd, className, className.Capacity);
                var name = className.ToString();
                if (name is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW"))
                    return true;
            }

            // The DWM frame excludes the invisible resize borders that GetWindowRect includes.
            if (Native.DwmGetWindowAttribute(hWnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r,
                    System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0)
                Native.GetWindowRect(hWnd, out r);

            var bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (bounds.Width > 1 && bounds.Height > 1)
                windows.Add(bounds);
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
