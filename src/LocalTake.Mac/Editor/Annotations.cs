using System.Globalization;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using LocalTake.Core.Editor;

namespace LocalTake.Mac.Editor;

/// <summary>
/// Something drawn over the capture, in image pixels. Each one draws itself with AppKit into the current graphics
/// context, the same way on screen and in the exported image. Moving, cropping and undo change <see cref="Offset"/>.
/// </summary>
internal abstract class Annotation
{
    public CGPoint Offset { get; set; }

    /// <summary>Where it is drawn, before the offset.</summary>
    public abstract CGRect Bounds { get; }

    public CGRect Frame => new(Bounds.X + Offset.X, Bounds.Y + Offset.Y, Bounds.Width, Bounds.Height);

    /// <summary>Whether a click at this point, in image pixels, takes the annotation.</summary>
    public virtual bool Contains(CGPoint p) => Frame.Inset(-4, -4).Contains(p);

    public void Draw()
    {
        NSGraphicsContext.GlobalSaveGraphicsState();
        var move = new NSAffineTransform();
        move.Translate(Offset.X, Offset.Y);
        move.Concat();
        DrawContent();
        NSGraphicsContext.GlobalRestoreGraphicsState();
    }

    protected abstract void DrawContent();

    /// <summary>The soft shadow of the shapes and texts, as on Windows.</summary>
    protected static void SetShadow()
    {
        using var shadow = new NSShadow
        {
            ShadowColor = NSColor.FromSrgb(0f, 0f, 0f, 0.45f),
            ShadowOffset = new CGSize(0, -1.5),
            ShadowBlurRadius = 4
        };
        shadow.Set();
    }
}

internal enum ShapeKind
{
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Pen,
    Highlighter
}

/// <summary>Arrows, lines, rectangles, ellipses and free strokes.</summary>
internal sealed class ShapeAnnotation(ShapeKind kind, NSColor color, double width) : Annotation
{
    private readonly List<CGPoint> _points = [];

    public ShapeKind Kind { get; } = kind;

    /// <summary>A rectangle or an ellipse: its frame. A line or an arrow: from, to. A stroke: every point.</summary>
    public IReadOnlyList<CGPoint> Points => _points;

    public double Width { get; } = width;

    public override CGRect Bounds
    {
        get
        {
            if (_points.Count == 0)
                return CGRect.Empty;
            double left = _points.Min(p => (double)p.X), top = _points.Min(p => (double)p.Y);
            double right = _points.Max(p => (double)p.X), bottom = _points.Max(p => (double)p.Y);
            var margin = Kind == ShapeKind.Arrow ? Math.Max(12, Width * 4) : Width / 2;
            return new CGRect(left - margin, top - margin, right - left + 2 * margin, bottom - top + 2 * margin);
        }
    }

    /// <summary>Too small to be meant: a click rather than a drag.</summary>
    public bool IsTooSmall => Kind switch
    {
        ShapeKind.Pen or ShapeKind.Highlighter => _points.Count < 2,
        ShapeKind.Rectangle or ShapeKind.Ellipse => _points.Count < 2 || Math.Abs(_points[1].X - _points[0].X) < 3 || Math.Abs(_points[1].Y - _points[0].Y) < 3,
        _ => _points.Count < 2 || Math.Abs(_points[1].X - _points[0].X) + Math.Abs(_points[1].Y - _points[0].Y) < 4
    };

    public void Start(CGPoint p)
    {
        _points.Clear();
        _points.Add(p);
    }

    /// <summary>The drag reached <paramref name="p"/>; Shift straightens lines and strokes and squares frames.</summary>
    public void DragTo(CGPoint start, CGPoint p, bool constrain)
    {
        switch (Kind)
        {
            case ShapeKind.Arrow or ShapeKind.Line:
                var end = constrain ? Snap45(start, p) : p;
                _points.Clear();
                _points.AddRange([start, end]);
                break;
            case ShapeKind.Rectangle or ShapeKind.Ellipse:
                var r = EditorGeometry.RectBetween(new PointD(start.X, start.Y), new PointD(p.X, p.Y), constrain);
                _points.Clear();
                _points.AddRange([new CGPoint(r.X, r.Y), new CGPoint(r.X + r.Width, r.Y + r.Height)]);
                break;
            default:
                // Shift draws a straight stroke.
                if (constrain && _points.Count > 1)
                    _points.RemoveRange(1, _points.Count - 1);
                _points.Add(p);
                break;
        }
    }

    private static CGPoint Snap45(CGPoint from, CGPoint to)
    {
        var p = EditorGeometry.Snap45(new PointD(from.X, from.Y), new PointD(to.X, to.Y));
        return new CGPoint(p.X, p.Y);
    }

    protected override void DrawContent()
    {
        if (_points.Count < 2 && Kind is not (ShapeKind.Pen or ShapeKind.Highlighter))
            return;
        var path = new NSBezierPath { LineWidth = (NFloat)Width, LineCapStyle = NSLineCapStyle.Round, LineJoinStyle = NSLineJoinStyle.Round };
        switch (Kind)
        {
            case ShapeKind.Arrow:
                DrawArrow();
                return;
            case ShapeKind.Line:
                path.MoveTo(_points[0]);
                path.LineTo(_points[1]);
                break;
            case ShapeKind.Rectangle:
                path = NSBezierPath.FromRect(RectOf(_points[0], _points[1]));
                path.LineWidth = (NFloat)Width;
                break;
            case ShapeKind.Ellipse:
                path = NSBezierPath.FromOvalInRect(RectOf(_points[0], _points[1]));
                path.LineWidth = (NFloat)Width;
                break;
            default:
                path.MoveTo(_points[0]);
                foreach (var p in _points.Skip(1))
                    path.LineTo(p);
                if (_points.Count == 1)
                    path.LineTo(_points[0]);
                break;
        }

        if (Kind == ShapeKind.Highlighter)
        {
            color.ColorWithAlphaComponent(110f / 255).SetStroke();
            path.LineCapStyle = NSLineCapStyle.Butt;
        }
        else
        {
            if (Kind != ShapeKind.Pen)
                SetShadow();
            color.SetStroke();
        }
        path.Stroke();
    }

    private void DrawArrow()
    {
        if (EditorGeometry.Arrow(new PointD(_points[0].X, _points[0].Y), new PointD(_points[1].X, _points[1].Y), Width) is not { } arrow)
            return;
        SetShadow();
        color.SetStroke();
        color.SetFill();
        var shaft = new NSBezierPath { LineWidth = (NFloat)Width, LineCapStyle = NSLineCapStyle.Round, LineJoinStyle = NSLineJoinStyle.Round };
        shaft.MoveTo(ToPoint(arrow.ShaftStart));
        shaft.LineTo(ToPoint(arrow.ShaftEnd));
        shaft.Stroke();
        var head = new NSBezierPath { LineWidth = (NFloat)Width, LineJoinStyle = NSLineJoinStyle.Round };
        head.MoveTo(ToPoint(arrow.Tip));
        head.LineTo(ToPoint(arrow.HeadLeft));
        head.LineTo(ToPoint(arrow.HeadRight));
        head.ClosePath();
        head.Fill();
        head.Stroke();
    }

    private static CGPoint ToPoint(PointD p) => new(p.X, p.Y);

    private static CGRect RectOf(CGPoint a, CGPoint b) => new(a.X, a.Y, b.X - a.X, b.Y - a.Y);
}

/// <summary>Text, bold, in one or several lines.</summary>
internal sealed class TextAnnotation(string text, CGPoint origin, double fontSize, NSColor color) : Annotation
{
    /// <summary>The padding of the text box it was typed in, so that the text does not jump.</summary>
    public static readonly CGSize Padding = new(3, 1);

    public string Text { get; } = text;

    public override CGRect Bounds
    {
        get
        {
            using var text = Attributed();
            var size = text.Size;
            return new CGRect(origin.X, origin.Y, size.Width + 2 * Padding.Width, size.Height + 2 * Padding.Height);
        }
    }

    protected override void DrawContent()
    {
        SetShadow();
        using var text = Attributed();
        text.DrawAtPoint(new CGPoint(origin.X + Padding.Width, origin.Y + Padding.Height));
    }

    private NSAttributedString Attributed() => new(Text, new NSStringAttributes
    {
        Font = NSFont.BoldSystemFontOfSize((NFloat)fontSize),
        ForegroundColor = color
    });
}

/// <summary>A numbered step badge.</summary>
internal sealed class NumberAnnotation(int value, CGPoint centre, double diameter, NSColor color) : Annotation
{
    public int Value { get; } = value;

    public override CGRect Bounds => new(centre.X - diameter / 2, centre.Y - diameter / 2, diameter, diameter);

    protected override void DrawContent()
    {
        SetShadow();
        var circle = NSBezierPath.FromOvalInRect(Bounds);
        color.SetFill();
        circle.Fill();
        NSGraphicsContext.GlobalSaveGraphicsState();
        using (var noShadow = new NSShadow())
            noShadow.Set();
        NSColor.White.SetStroke();
        circle.LineWidth = (NFloat)Math.Max(1.5, diameter / 16);
        circle.Stroke();

        var rgb = color.UsingColorSpace(NSColorSpace.SRGBColorSpace) ?? color;
        var dark = EditorGeometry.NeedsDarkText(Byte(rgb.RedComponent), Byte(rgb.GreenComponent), Byte(rgb.BlueComponent));
        using var text = new NSAttributedString(Value.ToString(CultureInfo.InvariantCulture), new NSStringAttributes
        {
            Font = NSFont.BoldSystemFontOfSize((NFloat)(diameter * 0.5)),
            ForegroundColor = dark ? NSColor.Black : NSColor.White
        });
        var size = text.Size;
        text.DrawAtPoint(new CGPoint(centre.X - size.Width / 2, centre.Y - size.Height / 2));
        NSGraphicsContext.GlobalRestoreGraphicsState();
    }

    private static byte Byte(NFloat component) => (byte)Math.Clamp(Math.Round(component * 255), 0, 255);
}

/// <summary>A pixelated area (blur), drawn without smoothing so that its blocks stay sharp at any zoom.</summary>
internal sealed class ImageAnnotation(NSImage image, CGRect area) : Annotation
{
    public override CGRect Bounds => area;

    protected override void DrawContent()
    {
        var context = NSGraphicsContext.CurrentContext;
        var interpolation = context?.ImageInterpolation ?? NSImageInterpolation.Default;
        if (context is not null)
            context.ImageInterpolation = NSImageInterpolation.None;
        image.Draw(area, CGRect.Empty, NSCompositingOperation.SourceOver, 1, true, null);
        if (context is not null)
            context.ImageInterpolation = interpolation;
    }
}

internal static class Colors
{
    /// <summary>"#RRGGBB" as an sRGB colour; red when unreadable.</summary>
    public static NSColor Parse(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return NSColor.FromSrgb(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        return NSColor.FromSrgb(0xE5 / 255f, 0x39 / 255f, 0x35 / 255f, 1f);
    }

    public static string ToHex(NSColor color)
    {
        var rgb = color.UsingColorSpace(NSColorSpace.SRGBColorSpace) ?? color;
        static int Byte(NFloat c) => (int)Math.Clamp(Math.Round(c * 255), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{Byte(rgb.RedComponent):X2}{Byte(rgb.GreenComponent):X2}{Byte(rgb.BlueComponent):X2}");
    }
}
