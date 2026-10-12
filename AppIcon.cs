using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalTake;

/// <summary>Icon drawn at startup: no binary resource to ship.</summary>
internal static class AppIcon
{
    private static Icon? _icon;
    private static ImageSource? _imageSource;

    public static Icon Icon => _icon ??= Create();

    public static ImageSource ImageSource => _imageSource ??= CreateImageSource();

    private static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = RoundedRect(new RectangleF(2, 2, 60, 60), 14);
            using var fill = new SolidBrush(System.Drawing.Color.FromArgb(10, 100, 173));
            g.FillPath(fill, background);

            // Selection corners around a dot: "capture".
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new PointF[] { new(14, 26), new(14, 14), new(26, 14) });
            g.DrawLines(pen, new PointF[] { new(38, 14), new(50, 14), new(50, 26) });
            g.DrawLines(pen, new PointF[] { new(50, 38), new(50, 50), new(38, 50) });
            g.DrawLines(pen, new PointF[] { new(26, 50), new(14, 50), new(14, 38) });
            using var dot = new SolidBrush(System.Drawing.Color.FromArgb(255, 87, 34));
            g.FillEllipse(dot, 24, 24, 16, 16);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    private static ImageSource CreateImageSource()
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
