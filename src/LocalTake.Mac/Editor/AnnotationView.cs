using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using LocalTake.Core.Editor;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Mac.Adapters;
using LocalTake.Platform.MacOS;
using ObjCRuntime;

namespace LocalTake.Mac.Editor;

internal enum Tool
{
    Select,
    Arrow,
    Line,
    Rectangle,
    Ellipse,
    Pen,
    Highlighter,
    Text,
    Number,
    Blur,
    Crop
}

/// <summary>
/// The capture and its annotations, sized in image pixels: the scroll view around it zooms. Export draws the same
/// annotations at the image's size, whatever the zoom.
/// </summary>
internal sealed class AnnotationView : NSView
{
    private static readonly NSColor SelectionColor = NSColor.FromSrgb(0f, 191f / 255, 1f, 1f);
    private static readonly NSColor GestureFill = NSColor.FromSrgb(10f / 255, 100f / 255, 173f / 255, 50f / 255);

    private static readonly Dictionary<string, Tool> ToolKeys = new()
    {
        ["v"] = Tool.Select,
        ["a"] = Tool.Arrow,
        ["l"] = Tool.Line,
        ["r"] = Tool.Rectangle,
        ["e"] = Tool.Ellipse,
        ["p"] = Tool.Pen,
        ["h"] = Tool.Highlighter,
        ["t"] = Tool.Text,
        ["n"] = Tool.Number,
        ["b"] = Tool.Blur,
        ["c"] = Tool.Crop
    };

    private readonly List<Annotation> _annotations = [];
    private readonly UndoHistory _history = new();
    private CGImage _base;
    private NSImage _baseImage;
    private PixelBuffer? _basePixels;
    private Tool _tool = Tool.Arrow;

    private CGPoint _start;
    private ShapeAnnotation? _preview;
    private CGRect? _frameGesture;
    private Annotation? _selected;
    private Annotation? _moving;
    private CGPoint _moveOrigin;
    private CGPoint _moveStartOffset;
    private NSTextField? _activeText;

    public AnnotationView(CGImage image)
    {
        _base = image;
        _baseImage = new NSImage(image, new CGSize(image.Width, image.Height));
        SetFrameSize(new CGSize(image.Width, image.Height));
        _history.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An annotation was added, moved or removed, or the image cropped; or undone, or redone.</summary>
    public event EventHandler? Changed;

    /// <summary>The image changed size (crop, or its undo).</summary>
    public event EventHandler? ImageResized;

    /// <summary>A tool was chosen with its key.</summary>
    public event EventHandler? ToolChanged;

    public int ImageWidth => (int)_base.Width;
    public int ImageHeight => (int)_base.Height;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public NSColor Color { get; set; } = NSColor.Red;
    public double Thickness { get; set; } = 4;
    public double FontSize { get; set; } = 24;

    /// <summary>
    /// Pixels per point of the screen. The image is in physical pixels: sizes chosen in the toolbar are multiplied by
    /// it, so that annotations look the same on a Retina screen as on another one.
    /// </summary>
    public double DpiScale { get; set; } = 1;

    private double StrokeWidth => Thickness * DpiScale;
    private double TextSize => FontSize * DpiScale;

    public Tool Tool
    {
        get => _tool;
        set
        {
            CommitText();
            Select(null);
            _tool = value;
            Window?.InvalidateCursorRectsForView(this);
        }
    }

    /// <summary>Top-left origin, as image pixels.</summary>
    public override bool IsFlipped => true;

    public override bool AcceptsFirstResponder() => true;

    public override bool AcceptsFirstMouse(NSEvent? theEvent) => true;

    public override void ResetCursorRects() => AddCursorRect(VisibleRect(), _tool switch
    {
        Tool.Select => NSCursor.ArrowCursor,
        Tool.Text => NSCursor.IBeamCursor,
        _ => NSCursor.CrosshairCursor
    });

    // ---- Mouse ----------------------------------------------------------------------------------------------

    public override void MouseDown(NSEvent theEvent)
    {
        CommitText();
        Window?.MakeFirstResponder(this);
        var p = PointOf(theEvent);
        _start = p;
        switch (_tool)
        {
            case Tool.Select:
                var target = AnnotationAt(p);
                Select(target);
                if (target is not null)
                {
                    _moving = target;
                    _moveOrigin = p;
                    _moveStartOffset = target.Offset;
                }
                break;
            case Tool.Text:
                StartText(p);
                break;
            case Tool.Number:
                AddNumber(p);
                break;
            case Tool.Blur or Tool.Crop:
                _frameGesture = new CGRect(p, CGSize.Empty);
                break;
            default:
                var kind = _tool switch
                {
                    Tool.Arrow => ShapeKind.Arrow,
                    Tool.Line => ShapeKind.Line,
                    Tool.Rectangle => ShapeKind.Rectangle,
                    Tool.Ellipse => ShapeKind.Ellipse,
                    Tool.Pen => ShapeKind.Pen,
                    _ => ShapeKind.Highlighter
                };
                var width = kind == ShapeKind.Highlighter ? Math.Max(14 * DpiScale, StrokeWidth * 4) : StrokeWidth;
                _preview = new ShapeAnnotation(kind, Color, width);
                _preview.Start(p);
                break;
        }
        NeedsDisplay = true;
    }

    public override void MouseDragged(NSEvent theEvent)
    {
        var p = PointOf(theEvent);
        var constrain = theEvent.ModifierFlags.HasFlag(NSEventModifierMask.ShiftKeyMask);
        if (_moving is not null)
            _moving.Offset = new CGPoint(_moveStartOffset.X + p.X - _moveOrigin.X, _moveStartOffset.Y + p.Y - _moveOrigin.Y);
        else if (_preview is not null)
            _preview.DragTo(_start, p, constrain);
        else if (_frameGesture is not null)
        {
            var r = EditorGeometry.RectBetween(new PointD(_start.X, _start.Y), new PointD(p.X, p.Y), square: false);
            _frameGesture = new CGRect(r.X, r.Y, r.Width, r.Height);
        }
        NeedsDisplay = true;
    }

    public override void MouseUp(NSEvent theEvent)
    {
        if (_moving is { } moved)
        {
            _moving = null;
            CGPoint from = _moveStartOffset, to = moved.Offset;
            if (from != to)
                Push(() => moved.Offset = from, () => moved.Offset = to);
        }
        else if (_preview is { } shape)
        {
            _preview = null;
            if (!shape.IsTooSmall)
                AddAnnotation(shape);
        }
        else if (_frameGesture is { } frame)
        {
            _frameGesture = null;
            var area = EditorGeometry.ToPixelRect(new RectD(frame.X, frame.Y, frame.Width, frame.Height), ImageWidth, ImageHeight);
            if (area is { } selected)
            {
                if (_tool == Tool.Blur)
                    AddBlur(selected);
                else
                    ApplyCrop(selected);
            }
        }
        NeedsDisplay = true;
    }

    private CGPoint PointOf(NSEvent theEvent) => ConvertPointFromView(theEvent.LocationInWindow, null);

    /// <summary>Escape during a drag: the moved annotation goes back, the shape being drawn is dropped.</summary>
    private bool CancelGesture()
    {
        var cancelled = _moving is not null || _preview is not null || _frameGesture is not null;
        if (_moving is { } moved)
            moved.Offset = _moveStartOffset;
        _moving = null;
        _preview = null;
        _frameGesture = null;
        NeedsDisplay = true;
        return cancelled;
    }

    // ---- Keyboard -------------------------------------------------------------------------------------------

    public override void KeyDown(NSEvent theEvent)
    {
        const ushort escape = 53, backspace = 51, forwardDelete = 117;
        var gesture = _moving is not null || _preview is not null || _frameGesture is not null;
        // Undo, delete or a tool change in the middle of a drag would leave the gesture half applied.
        if (gesture)
        {
            if (theEvent.KeyCode == escape)
                CancelGesture();
            return;
        }
        var modifiers = theEvent.ModifierFlags & (NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask |
            NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask);
        if (modifiers != 0)
        {
            base.KeyDown(theEvent);
            return;
        }
        if (theEvent.KeyCode is backspace or forwardDelete)
            DeleteSelected();
        else if (theEvent.KeyCode == escape)
            Select(null);
        else if (ToolKeys.TryGetValue(theEvent.CharactersIgnoringModifiers?.ToLowerInvariant() ?? "", out var tool))
        {
            Tool = tool;
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
        else
            base.KeyDown(theEvent);
    }

    // ---- Menu actions (first responder) ----------------------------------------------------------------------

    [Export("undo:")]
    public void Undo(NSObject? sender)
    {
        CommitText();
        Select(null);
        _history.Undo();
        NeedsDisplay = true;
    }

    [Export("redo:")]
    public void Redo(NSObject? sender)
    {
        CommitText();
        Select(null);
        _history.Redo();
        NeedsDisplay = true;
    }

    /// <summary>Copies the image, through the editor window controller.</summary>
    [Export("copy:")]
    public void Copy(NSObject? sender) => CopyRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? CopyRequested;

    [Export("validateMenuItem:")]
    public bool ValidateMenu(NSMenuItem item) => item.Action?.Name switch
    {
        "undo:" => _history.CanUndo,
        "redo:" => _history.CanRedo,
        _ => true
    };

    // ---- Annotations ----------------------------------------------------------------------------------------

    private Annotation? AnnotationAt(CGPoint p)
    {
        for (var i = _annotations.Count - 1; i >= 0; i--)
            if (_annotations[i].Contains(p))
                return _annotations[i];
        return null;
    }

    private void Select(Annotation? annotation)
    {
        _selected = annotation;
        NeedsDisplay = true;
    }

    private void AddAnnotation(Annotation annotation)
    {
        _annotations.Add(annotation);
        Push(() => _annotations.Remove(annotation), () => _annotations.Add(annotation));
    }

    private void DeleteSelected()
    {
        if (_selected is not { } annotation)
            return;
        Select(null);
        var index = _annotations.IndexOf(annotation);
        _annotations.Remove(annotation);
        Push(() => _annotations.Insert(Math.Min(index, _annotations.Count), annotation), () => _annotations.Remove(annotation));
    }

    private void Push(Action undo, Action redo)
    {
        _history.Push(() =>
        {
            undo();
            NeedsDisplay = true;
        }, () =>
        {
            redo();
            NeedsDisplay = true;
        });
        NeedsDisplay = true;
    }

    private void AddNumber(CGPoint p)
    {
        var number = EditorGeometry.NextStepNumber(_annotations.OfType<NumberAnnotation>().Select(n => n.Value));
        var diameter = Math.Max(24 * DpiScale, TextSize * 1.25);
        AddAnnotation(new NumberAnnotation(number, p, diameter, Color));
    }

    /// <summary>The core's pixelation of the image, the same as on Windows, placed as a movable annotation.</summary>
    internal void AddBlur(PixelRect area)
    {
        var (columns, rows) = EditorGeometry.PixelationGrid(area, EditorGeometry.PixelationBlock(area, DpiScale));
        _basePixels ??= MacImage.ToPixels(_base);
        using var pixelated = MacImage.FromPixels(_basePixels.Pixelate(area, columns, rows));
        var image = new NSImage(pixelated.Image, new CGSize(area.Width, area.Height));
        AddAnnotation(new ImageAnnotation(image, new CGRect(area.X, area.Y, area.Width, area.Height)));
    }

    /// <summary>Crop does not flatten: the image is swapped and every annotation shifted, so both stay undoable.</summary>
    private void ApplyCrop(PixelRect area)
    {
        var oldBase = _base;
        var newBase = oldBase.WithImageInRect(new CGRect(area.X, area.Y, area.Width, area.Height));
        if (newBase is null)
            return;
        var items = _annotations.ToList();

        void Shift(int dx, int dy)
        {
            foreach (var item in items)
                item.Offset = new CGPoint(item.Offset.X + dx, item.Offset.Y + dy);
        }

        void Apply()
        {
            SetBase(newBase);
            Shift(-area.X, -area.Y);
        }

        void Revert()
        {
            SetBase(oldBase);
            Shift(area.X, area.Y);
        }

        Apply();
        Push(Revert, Apply);
    }

    private void SetBase(CGImage image)
    {
        _base = image;
        _baseImage.Dispose();
        _baseImage = new NSImage(image, new CGSize(image.Width, image.Height));
        _basePixels = null;
        SetFrameSize(new CGSize(image.Width, image.Height));
        NeedsDisplay = true;
        ImageResized?.Invoke(this, EventArgs.Empty);
    }

    // ---- Text -----------------------------------------------------------------------------------------------

    private void StartText(CGPoint p)
    {
        var size = TextSize;
        var field = new NSTextField(new CGRect(p.X, p.Y, 60, size * 1.4))
        {
            TextColor = Color,
            BackgroundColor = NSColor.FromSrgb(1f, 1f, 1f, 60f / 255),
            DrawsBackground = true,
            Bordered = true,
            Bezeled = false,
            Editable = true,
            Selectable = true,
            FocusRingType = NSFocusRingType.None,
            UsesSingleLineMode = false,
            ToolTip = "Entrée : valider · Maj+Entrée : nouvelle ligne"
        };
        if (NSFont.BoldSystemFontOfSize((NFloat)size) is { } font)
            field.Font = font;
        field.Changed += (_, _) => Fit(field);
        field.EditingEnded += (_, _) => CommitText();
        // Enter and Escape finish the text; Shift+Enter (or Option+Enter) starts a new line.
        field.DoCommandBySelector = (_, textView, selector) =>
        {
            switch (selector.Name)
            {
                case "insertNewline:" when NSApplication.SharedApplication.CurrentEvent?.ModifierFlags.HasFlag(NSEventModifierMask.ShiftKeyMask) == true:
                    NSApplication.SharedApplication.SendAction(new Selector("insertNewlineIgnoringFieldEditor:"), textView, textView);
                    Fit(field);
                    return true;
                case "insertNewline:" or "cancelOperation:":
                    CommitText();
                    return true;
                default:
                    return false;
            }
        };
        AddSubview(field);
        _activeText = field;
        Window?.MakeFirstResponder(field);
    }

    private static void Fit(NSTextField field)
    {
        var origin = field.Frame.Location;
        field.SizeToFit();
        var size = field.Frame.Size;
        field.Frame = new CGRect(origin, new CGSize(Math.Max(60, (double)size.Width + 8), size.Height));
    }

    /// <summary>Turns the text being typed into an annotation.</summary>
    public void CommitText()
    {
        if (_activeText is not { } field)
            return;
        _activeText = null;
        var text = field.StringValue;
        var origin = field.Frame.Location;
        var font = field.Font?.PointSize ?? (NFloat)TextSize;
        var color = field.TextColor ?? Color;
        if (Window?.FirstResponder == field.CurrentEditor)
            Window?.MakeFirstResponder(this);
        field.RemoveFromSuperview();
        field.Dispose();
        if (!string.IsNullOrWhiteSpace(text))
            AddAnnotation(new TextAnnotation(text, origin, font, color));
    }

    // ---- Drawing --------------------------------------------------------------------------------------------

    public override void DrawRect(CGRect dirtyRect)
    {
        DrawDocument();
        _preview?.Draw();
        if (_frameGesture is { } frame)
        {
            var path = NSBezierPath.FromRect(frame);
            GestureFill.SetFill();
            path.Fill();
            NSColor.White.SetStroke();
            path.LineWidth = 1.5f;
            path.SetLineDash([4, 3], 0);
            path.Stroke();
        }
        if (_selected is { } selected)
        {
            var path = NSBezierPath.FromRect(selected.Frame.Inset(-4, -4));
            SelectionColor.SetStroke();
            path.LineWidth = 1.5f;
            path.SetLineDash([4, 3], 0);
            path.Stroke();
        }
    }

    /// <summary>The image and its annotations: what the export contains.</summary>
    private void DrawDocument()
    {
        _baseImage.Draw(new CGRect(0, 0, ImageWidth, ImageHeight), CGRect.Empty, NSCompositingOperation.Copy, 1, true, null);
        foreach (var annotation in _annotations)
            annotation.Draw();
    }

    /// <summary>The capture with its annotations, at the image's pixel size, whatever the zoom.</summary>
    internal PixelBuffer RenderImage()
    {
        CommitText();
        int width = ImageWidth, height = ImageHeight;
        var pixels = new byte[width * height * 4];
        using (var colorSpace = CGColorSpace.CreateSrgb())
        using (var context = new CGBitmapContext(pixels, width, height, 8, width * 4, colorSpace,
                   CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little))
        {
            // Top-left origin, as on screen.
            context.TranslateCTM(0, height);
            context.ScaleCTM(1, -1);
            NSGraphicsContext.GlobalSaveGraphicsState();
            NSGraphicsContext.CurrentContext = NSGraphicsContext.FromCGContext(context, true);
            DrawDocument();
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }
        MacPixels.Unpremultiply(pixels);
        return new PixelBuffer(width, height, pixels);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _baseImage.Dispose();
        base.Dispose(disposing);
    }
}
