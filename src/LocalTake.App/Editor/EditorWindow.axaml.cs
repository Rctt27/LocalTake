using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LocalTake.App.Adapters;
using LocalTake.Core.Editor;
using LocalTake.Core.Files;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;
using CorePixelRect = LocalTake.Core.Geometry.PixelRect;
using Path = Avalonia.Controls.Shapes.Path;

namespace LocalTake.App.Editor;

/// <summary>What the editor needs from the rest of the app.</summary>
internal sealed record EditorContext(
    AppSettings Settings, ISettingsStore Store, ImageSaver Saver, IClipboard Clipboard, IShell Shell, IUserDialogs Dialogs);

internal enum EditorTool { Select, Arrow, Line, Rectangle, Ellipse, Pen, Highlighter, Text, Number, Blur, Crop }

/// <summary>
/// Annotation editor. The canvas is sized in image pixels, annotations are ordinary controls positioned on it, and
/// export renders the canvas back at the image's pixel size, whatever the zoom.
/// </summary>
public partial class EditorWindow : Window
{
    private static readonly string[] Palette = ["#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF"];
    private static readonly double[] Thicknesses = [2, 3, 4, 6, 8, 12];
    private static readonly double[] FontSizes = [14, 18, 24, 32, 48, 64];

    private static readonly Dictionary<Key, EditorTool> ToolKeys = new()
    {
        [Key.V] = EditorTool.Select,
        [Key.A] = EditorTool.Arrow,
        [Key.L] = EditorTool.Line,
        [Key.R] = EditorTool.Rectangle,
        [Key.E] = EditorTool.Ellipse,
        [Key.P] = EditorTool.Pen,
        [Key.H] = EditorTool.Highlighter,
        [Key.T] = EditorTool.Text,
        [Key.N] = EditorTool.Number,
        [Key.B] = EditorTool.Blur,
        [Key.C] = EditorTool.Crop
    };

    /// <summary>Ctrl on Windows, Cmd on a Mac.</summary>
    private static readonly KeyModifiers CommandKey = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private readonly EditorContext _context = null!;
    private readonly AppSettings _settings = null!;
    private readonly UndoHistory _history = new();
    private readonly ScaleTransform _zoom = new(1, 1);
    private PixelBuffer _base = null!;
    private EditorTool _tool = EditorTool.Arrow;
    private Color _color;
    private double _thickness;
    private double _fontSize;
    private double _dpiScale = 1;

    /// <summary>A fresh capture counts as unsaved: closing it would lose it.</summary>
    private bool _dirty = true;
    private bool _closeConfirmed;

    private Point _start;
    private Control? _preview;
    private bool _drawing;

    private Control? _selected;
    /// <summary>The annotation being dragged with the Select tool.</summary>
    private Control? _moving;
    private Point _moveOrigin;
    private Vector _moveStartOffset;

    private TextBox? _activeText;

    /// <summary>For the XAML previewer.</summary>
    public EditorWindow() => InitializeComponent();

    internal EditorWindow(PixelBuffer image, EditorContext context)
    {
        InitializeComponent();
        _context = context;
        _settings = context.Settings;
        _color = ParseColor(_settings.EditorColor);
        _thickness = _settings.EditorThickness;
        _fontSize = _settings.EditorFontSize;

        Icon = AppIcon.Window;
        Title = $"LocalTake — {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        ZoomHost.LayoutTransform = _zoom;
        SetBase(image);
        BuildToolbar();

        Surface.PointerPressed += Surface_PointerPressed;
        Surface.PointerMoved += Surface_PointerMoved;
        Surface.PointerReleased += Surface_PointerReleased;
        // Alt+Tab or a system dialog in the middle of a drag: no release will come.
        Surface.PointerCaptureLost += (_, _) => CancelGesture();
        Scroller.AddHandler(PointerWheelChangedEvent, Scroller_PointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            _dpiScale = RenderScaling;
            SizeWindowToImage();
            Dispatcher.UIThread.Post(() => ZoomToFit(allowEnlarge: false), DispatcherPriority.Loaded);
        };
        ScalingChanged += (_, _) =>
        {
            _dpiScale = RenderScaling;
            UpdateZoomText();
        };
        Closing += Window_Closing;
        Closed += (_, _) => SavePreferences();

        UpdateUndoButtons();
        StatusText.Text = $"Annotez, puis enregistrez ({Shortcut("S")}) ou copiez ({Shortcut("C")}).";
    }

    // The canvas is in image pixels, and captures are in physical pixels: sizes chosen in the toolbar are scaled like
    // the rest of the screen so that annotations keep the same look on a 150 % or 200 % display.
    private double StrokeWidth => _thickness * _dpiScale;
    private double TextSize => _fontSize * _dpiScale;

    private static string Shortcut(string key) => (OperatingSystem.IsMacOS() ? "Cmd+" : "Ctrl+") + key;

    // ---- Setup ----------------------------------------------------------------------------------------------

    private void BuildToolbar()
    {
        foreach (var button in Toolbar.Children.OfType<ToggleButton>().Where(b => b.Classes.Contains("tool")))
        {
            var tool = Enum.Parse<EditorTool>((string)button.Tag!);
            button.IsChecked = tool == _tool;
            button.Click += (_, _) => SelectTool(tool);
        }

        foreach (var hex in Palette)
        {
            var color = ParseColor(hex);
            var button = new ToggleButton
            {
                Classes = { "tool" },
                Padding = new Thickness(4),
                IsChecked = color == _color,
                Tag = "color",
                Content = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(color), Stroke = Brushes.Gray, StrokeThickness = 1 }
            };
            ToolTip.SetTip(button, hex);
            button.Click += (_, _) =>
            {
                _color = color;
                foreach (var other in ColorPanel.Children.OfType<ToggleButton>())
                    other.IsChecked = other == button;
            };
            ColorPanel.Children.Add(button);
        }

        ThicknessBox.ItemsSource = Thicknesses;
        ThicknessBox.SelectedItem = EditorGeometry.Nearest(Thicknesses, _thickness);
        ThicknessBox.SelectionChanged += (_, _) => _thickness = (double)ThicknessBox.SelectedItem!;
        FontSizeBox.ItemsSource = FontSizes;
        FontSizeBox.SelectedItem = EditorGeometry.Nearest(FontSizes, _fontSize);
        FontSizeBox.SelectionChanged += (_, _) => _fontSize = (double)FontSizeBox.SelectedItem!;

        ToolTip.SetTip(UndoButton, $"Annuler ({Shortcut("Z")})");
        ToolTip.SetTip(RedoButton, $"Rétablir ({Shortcut("Y")})");
        ToolTip.SetTip(CopyButton, $"Copier l'image ({Shortcut("C")})");
        ToolTip.SetTip(SaveAsButton, $"Enregistrer sous… ({Shortcut("Maj+S")})");
        ToolTip.SetTip(SaveButton, $"Enregistrer dans le dossier des captures ({Shortcut("S")})");
        ToolTip.SetTip(ZoomActualButton, $"Taille réelle ({Shortcut("1")})");
        ToolTip.SetTip(ZoomFitButton, $"Ajuster à la fenêtre ({Shortcut("0")})");
        UndoButton.Click += (_, _) => Undo();
        RedoButton.Click += (_, _) => Redo();
        CopyButton.Click += (_, _) => Copy();
        SaveAsButton.Click += async (_, _) => await SaveAsAsync();
        SaveButton.Click += (_, _) => Save();
        ZoomInButton.Click += (_, _) => SetZoom(_zoom.ScaleX * 1.25);
        ZoomOutButton.Click += (_, _) => SetZoom(_zoom.ScaleX / 1.25);
        ZoomActualButton.Click += (_, _) => SetZoom(1 / _dpiScale);
        ZoomFitButton.Click += (_, _) => ZoomToFit(allowEnlarge: true);
    }

    private void SizeWindowToImage()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen)
            return;
        var work = screen.WorkingArea;
        var scaling = screen.Scaling;
        Width = Math.Max(MinWidth, Math.Min(_base.Width / _dpiScale + 80, work.Width / scaling * 0.9));
        Height = Math.Max(MinHeight, Math.Min(_base.Height / _dpiScale + 150, work.Height / scaling * 0.9));
        Position = new PixelPoint(
            work.X + (int)((work.Width - Width * scaling) / 2),
            work.Y + (int)((work.Height - Height * scaling) / 2));
    }

    private void SetBase(PixelBuffer image)
    {
        _base = image;
        var old = BaseImage.Source as IDisposable;
        BaseImage.Source = ImageData.ToBitmap(image);
        old?.Dispose();
        BaseImage.Width = Surface.Width = SelectionLayer.Width = image.Width;
        BaseImage.Height = Surface.Height = SelectionLayer.Height = image.Height;
        SizeText.Text = $"{image.Width} × {image.Height} px";
    }

    // ---- Zoom -----------------------------------------------------------------------------------------------

    private void SetZoom(double zoom)
    {
        zoom = EditorGeometry.ClampZoom(zoom);
        _zoom.ScaleX = _zoom.ScaleY = zoom;
        UpdateZoomText();
    }

    /// <summary>Shown relative to screen pixels, so 100 % means the capture at its real size.</summary>
    private void UpdateZoomText() =>
        ZoomText.Text = Math.Round(_zoom.ScaleX * _dpiScale * 100).ToString(CultureInfo.CurrentCulture) + " %";

    private void ZoomToFit(bool allowEnlarge) =>
        SetZoom(EditorGeometry.FitZoom(Scroller.Bounds.Width, Scroller.Bounds.Height, _base.Width, _base.Height, _dpiScale, allowEnlarge));

    private void Scroller_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(CommandKey))
            return;
        SetZoom(_zoom.ScaleX * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    // ---- Tools ----------------------------------------------------------------------------------------------

    private void SelectTool(EditorTool tool)
    {
        CommitText();
        Select(null);
        _tool = tool;
        foreach (var button in Toolbar.Children.OfType<ToggleButton>().Where(b => b.Classes.Contains("tool")))
            button.IsChecked = (string)button.Tag! == tool.ToString();
        Surface.Cursor = new Cursor(tool switch
        {
            EditorTool.Select => StandardCursorType.Arrow,
            EditorTool.Text => StandardCursorType.Ibeam,
            _ => StandardCursorType.Cross
        });
    }

    private void Surface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Surface).Properties.IsLeftButtonPressed)
            return;
        if (_activeText is not null && IsWithin(e.Source as Visual, _activeText))
            return;
        CommitText();

        var p = e.GetPosition(Surface);
        _start = p;
        e.Handled = true;

        switch (_tool)
        {
            case EditorTool.Select:
                var target = FindAnnotation(e.Source as Visual);
                Select(target);
                if (target is not null)
                {
                    _moving = target;
                    _moveOrigin = p;
                    _moveStartOffset = GetOffset(target);
                    e.Pointer.Capture(Surface);
                }
                break;
            case EditorTool.Text:
                StartText(p);
                break;
            case EditorTool.Number:
                AddNumber(p);
                break;
            default:
                _preview = CreateShape(_tool, p);
                Surface.Children.Add(_preview);
                _drawing = true;
                e.Pointer.Capture(Surface);
                break;
        }
    }

    private void Surface_PointerMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(Surface);
        if (_moving is not null)
        {
            SetOffset(_moving, _moveStartOffset + (p - _moveOrigin));
            return;
        }
        if (!_drawing || _preview is null)
            return;

        var constrain = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (_tool)
        {
            case EditorTool.Arrow:
                ((Path)_preview).Data = ArrowGeometry(_start, constrain ? Snap45(_start, p) : p, StrokeWidth);
                break;
            case EditorTool.Line:
                ((Line)_preview).EndPoint = constrain ? Snap45(_start, p) : p;
                break;
            case EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Blur or EditorTool.Crop:
                var r = EditorGeometry.RectBetween(ToCore(_start), ToCore(p), square: constrain && _tool is EditorTool.Rectangle or EditorTool.Ellipse);
                Canvas.SetLeft(_preview, r.X);
                Canvas.SetTop(_preview, r.Y);
                _preview.Width = r.Width;
                _preview.Height = r.Height;
                break;
            case EditorTool.Pen or EditorTool.Highlighter:
                var line = (Polyline)_preview;
                // Shift draws a straight stroke.
                var points = constrain ? new Points { line.Points[0] } : new Points(line.Points);
                points.Add(p);
                line.Points = points;
                break;
        }
    }

    private void Surface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (_moving is { } element)
        {
            _moving = null;
            e.Pointer.Capture(null);
            var from = _moveStartOffset;
            var to = GetOffset(element);
            if (from != to)
                Push(() => SetOffset(element, from), () => SetOffset(element, to));
            return;
        }
        if (!_drawing || _preview is null)
            return;

        _drawing = false;
        var shape = _preview;
        _preview = null;
        e.Pointer.Capture(null);

        switch (_tool)
        {
            case EditorTool.Blur:
                Surface.Children.Remove(shape);
                if (ToPixelRect(shape) is { } blurArea)
                    AddAnnotation(CreatePixelated(blurArea));
                break;
            case EditorTool.Crop:
                Surface.Children.Remove(shape);
                if (ToPixelRect(shape) is { } cropArea)
                    ApplyCrop(cropArea);
                break;
            default:
                if (IsTooSmall(shape))
                    Surface.Children.Remove(shape);
                else
                    Push(() => Surface.Children.Remove(shape), () => Surface.Children.Add(shape));
                break;
        }
    }

    /// <summary>Escape during a drag: the moved annotation goes back, the shape being drawn is dropped.</summary>
    private void CancelGesture()
    {
        if (_moving is { } element)
        {
            SetOffset(element, _moveStartOffset);
            _moving = null;
        }
        if (_drawing && _preview is not null)
        {
            Surface.Children.Remove(_preview);
            _preview = null;
            _drawing = false;
        }
    }

    private Control CreateShape(EditorTool tool, Point p)
    {
        var brush = new SolidColorBrush(_color);
        Control shape = tool switch
        {
            EditorTool.Arrow => new Path { Stroke = brush, Fill = brush, StrokeThickness = StrokeWidth, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round, Effect = Shadow() },
            EditorTool.Line => new Line { StartPoint = p, EndPoint = p, Stroke = brush, StrokeThickness = StrokeWidth, StrokeLineCap = PenLineCap.Round, Effect = Shadow() },
            EditorTool.Rectangle => new Rectangle { Stroke = brush, StrokeThickness = StrokeWidth, Effect = Shadow() },
            EditorTool.Ellipse => new Ellipse { Stroke = brush, StrokeThickness = StrokeWidth, Effect = Shadow() },
            EditorTool.Pen => new Polyline { Stroke = brush, StrokeThickness = StrokeWidth, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round, Points = new Points { p } },
            EditorTool.Highlighter => new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(110, _color.R, _color.G, _color.B)),
                StrokeThickness = Math.Max(14 * _dpiScale, StrokeWidth * 4),
                StrokeJoin = PenLineJoin.Round,
                Points = new Points { p }
            },
            _ => new Rectangle
            {
                // Blur and crop: a temporary selection frame.
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(50, 10, 100, 173))
            }
        };
        if (shape is Rectangle or Ellipse)
        {
            Canvas.SetLeft(shape, p.X);
            Canvas.SetTop(shape, p.Y);
            shape.Width = 0;
            shape.Height = 0;
        }
        shape.RenderTransform = new TranslateTransform();
        return shape;
    }

    private static bool IsTooSmall(Control shape) => shape switch
    {
        Path path => path.Data is null || path.Data.Bounds.Width + path.Data.Bounds.Height < 4,
        Line line => Math.Abs(line.EndPoint.X - line.StartPoint.X) + Math.Abs(line.EndPoint.Y - line.StartPoint.Y) < 4,
        Polyline polyline => polyline.Points.Count < 2,
        _ => shape.Width < 3 || shape.Height < 3
    };

    private static DropShadowEffect Shadow() => new() { Color = Colors.Black, BlurRadius = 4, OffsetX = 1.06, OffsetY = 1.06, Opacity = 0.45 };

    private static StreamGeometry ArrowGeometry(Point from, Point to, double thickness)
    {
        var geometry = new StreamGeometry();
        if (EditorGeometry.Arrow(ToCore(from), ToCore(to), thickness) is not { } arrow)
            return geometry;
        using var context = geometry.Open();
        context.BeginFigure(ToAvalonia(arrow.ShaftStart), isFilled: false);
        context.LineTo(ToAvalonia(arrow.ShaftEnd));
        context.EndFigure(isClosed: false);
        context.BeginFigure(ToAvalonia(arrow.Tip), isFilled: true);
        context.LineTo(ToAvalonia(arrow.HeadLeft));
        context.LineTo(ToAvalonia(arrow.HeadRight));
        context.EndFigure(isClosed: true);
        return geometry;
    }

    private static PointD ToCore(Point p) => new(p.X, p.Y);

    private static Point ToAvalonia(PointD p) => new(p.X, p.Y);

    private static Point Snap45(Point from, Point to) => ToAvalonia(EditorGeometry.Snap45(ToCore(from), ToCore(to)));

    /// <summary>The frame of a blur/crop gesture as whole image pixels, or null when too small.</summary>
    private CorePixelRect? ToPixelRect(Control frame) =>
        EditorGeometry.ToPixelRect(new RectD(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height), _base.Width, _base.Height);

    /// <summary>Blur is pixelation of the image as it is now; it then moves like any annotation.</summary>
    private Image CreatePixelated(CorePixelRect area)
    {
        var (columns, rows) = EditorGeometry.PixelationGrid(area, EditorGeometry.PixelationBlock(area, _dpiScale));
        var image = new Image { Source = ImageData.ToBitmap(_base.Pixelate(area, columns, rows)), Width = area.Width, Height = area.Height };
        Canvas.SetLeft(image, area.X);
        Canvas.SetTop(image, area.Y);
        image.RenderTransform = new TranslateTransform();
        return image;
    }

    /// <summary>Crop does not flatten: the base image changes and every annotation shifts, so all stay editable.</summary>
    private void ApplyCrop(CorePixelRect area)
    {
        var oldBase = _base;
        var newBase = oldBase.Crop(area);
        var items = Annotations().ToList();
        var shift = new Vector(area.X, area.Y);

        void Apply()
        {
            SetBase(newBase);
            foreach (var item in items)
                SetOffset(item, GetOffset(item) - shift);
        }

        void Revert()
        {
            SetBase(oldBase);
            foreach (var item in items)
                SetOffset(item, GetOffset(item) + shift);
        }

        Apply();
        Push(Revert, Apply);
        Dispatcher.UIThread.Post(() => ZoomToFit(allowEnlarge: false), DispatcherPriority.Loaded);
    }

    private void AddNumber(Point p)
    {
        var number = EditorGeometry.NextStepNumber(Surface.Children.Select(c => c.Tag).OfType<StepNumber>().Select(n => n.Value));
        var diameter = Math.Max(24 * _dpiScale, TextSize * 1.25);
        var badge = new Grid { Width = diameter, Height = diameter, Tag = new StepNumber(number), Effect = Shadow() };
        badge.Children.Add(new Ellipse { Fill = new SolidColorBrush(_color), Stroke = Brushes.White, StrokeThickness = Math.Max(1.5, diameter / 16) });
        badge.Children.Add(new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            Foreground = EditorGeometry.NeedsDarkText(_color.R, _color.G, _color.B) ? Brushes.Black : Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = diameter * 0.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Canvas.SetLeft(badge, p.X - diameter / 2);
        Canvas.SetTop(badge, p.Y - diameter / 2);
        badge.RenderTransform = new TranslateTransform();
        AddAnnotation(badge);
    }

    private void StartText(Point p)
    {
        var box = new TextBox
        {
            MinWidth = 60,
            FontSize = TextSize,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(_color),
            CaretBrush = new SolidColorBrush(_color),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderBrush = Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3, 1),
            AcceptsReturn = true
        };
        ToolTip.SetTip(box, "Entrée : valider · Maj+Entrée : nouvelle ligne");
        Canvas.SetLeft(box, p.X);
        Canvas.SetTop(box, p.Y);
        box.LostFocus += (_, _) => CommitText();
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
            {
                CommitText();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        Surface.Children.Add(box);
        _activeText = box;
        Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Turns the text being typed into a regular annotation.</summary>
    private void CommitText()
    {
        var box = _activeText;
        if (box is null)
            return;
        _activeText = null;
        Surface.Children.Remove(box);
        if (string.IsNullOrWhiteSpace(box.Text))
            return;

        var block = new TextBlock
        {
            Text = box.Text,
            FontSize = box.FontSize,
            FontWeight = FontWeight.Bold,
            Foreground = box.Foreground,
            // Matches the text box border and padding so that the text does not jump.
            Padding = new Thickness(4, 2),
            Effect = Shadow()
        };
        Canvas.SetLeft(block, Canvas.GetLeft(box));
        Canvas.SetTop(block, Canvas.GetTop(box));
        block.RenderTransform = new TranslateTransform();
        AddAnnotation(block);
    }

    // ---- Annotations, selection, undo -----------------------------------------------------------------------

    private IEnumerable<Control> Annotations() => Surface.Children.Where(c => c != BaseImage && c != _preview && c != _activeText);

    private void AddAnnotation(Control element)
    {
        Surface.Children.Add(element);
        Push(() => Surface.Children.Remove(element), () => Surface.Children.Add(element));
    }

    private void DeleteSelected()
    {
        if (_selected is not { } element)
            return;
        Select(null);
        var index = Surface.Children.IndexOf(element);
        Surface.Children.Remove(element);
        Push(() => Surface.Children.Insert(Math.Min(index, Surface.Children.Count), element), () => Surface.Children.Remove(element));
    }

    private Control? FindAnnotation(Visual? source)
    {
        while (source is not null)
        {
            var parent = source.GetVisualParent();
            if (parent == Surface)
                return source == BaseImage ? null : source as Control;
            source = parent;
        }
        return null;
    }

    private static bool IsWithin(Visual? source, Visual ancestor)
    {
        for (; source is not null; source = source.GetVisualParent())
            if (source == ancestor)
                return true;
        return false;
    }

    private void Select(Control? element)
    {
        _selected = element;
        UpdateSelectionFrame();
    }

    /// <summary>A dashed frame around the selected annotation, on a layer that is not exported.</summary>
    private void UpdateSelectionFrame()
    {
        SelectionLayer.Children.Clear();
        if (_selected is not { } element || AnnotationBounds(element) is not { } bounds)
            return;
        var frame = new Rectangle
        {
            Width = bounds.Width + 8,
            Height = bounds.Height + 8,
            Stroke = Brushes.DeepSkyBlue,
            StrokeThickness = 1.5 * _dpiScale,
            StrokeDashArray = new AvaloniaList<double> { 4, 2 }
        };
        Canvas.SetLeft(frame, bounds.X - 4);
        Canvas.SetTop(frame, bounds.Y - 4);
        SelectionLayer.Children.Add(frame);
    }

    private static Rect? AnnotationBounds(Control element)
    {
        var offset = GetOffset(element);
        Rect rect = element switch
        {
            Path { Data: { } data } => data.Bounds,
            Line line => new Rect(line.StartPoint, line.EndPoint).Normalize(),
            Polyline polyline when polyline.Points.Count > 0 => polyline.Points.Skip(1).Aggregate(new Rect(polyline.Points[0], new Size()), (r, p) => r.Union(new Rect(p, new Size()))),
            _ => new Rect(Canvas.GetLeft(element), Canvas.GetTop(element), element.Bounds.Width, element.Bounds.Height)
        };
        return rect.Translate(offset);
    }

    private static Vector GetOffset(Control element) =>
        element.RenderTransform is TranslateTransform t ? new Vector(t.X, t.Y) : default;

    private void SetOffset(Control element, Vector offset)
    {
        if (element.RenderTransform is not TranslateTransform t)
            element.RenderTransform = t = new TranslateTransform();
        t.X = offset.X;
        t.Y = offset.Y;
        if (element == _selected)
            UpdateSelectionFrame();
    }

    private void Push(Action undo, Action redo)
    {
        _history.Push(undo, redo);
        _dirty = true;
        UpdateUndoButtons();
    }

    private void Undo()
    {
        CommitText();
        Select(null);
        if (_history.Undo())
            _dirty = true;
        UpdateUndoButtons();
    }

    private void Redo()
    {
        CommitText();
        Select(null);
        if (_history.Redo())
            _dirty = true;
        UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
    }

    // ---- Output ---------------------------------------------------------------------------------------------

    /// <summary>The capture with its annotations, at the image's pixel size.</summary>
    internal PixelBuffer RenderImage()
    {
        CommitText();
        Surface.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize(_base.Width, _base.Height), new Vector(96, 96));
        bitmap.Render(Surface);
        return ImageData.FromBitmap(bitmap);
    }

    private bool Save()
    {
        try
        {
            ShowSaved(_context.Saver.SaveToOutputFolder(new ImageData(RenderImage())));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _context.Dialogs.ShowError("Enregistrement impossible : " + ex.Message);
            return false;
        }
    }

    private async Task SaveAsAsync()
    {
        try
        {
            if (await _context.Saver.SaveAsAsync(new ImageData(RenderImage()), new StorageSaveDialog(this)) is { } path)
                ShowSaved(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _context.Dialogs.ShowError("Enregistrement impossible : " + ex.Message);
        }
    }

    private void Copy()
    {
        try
        {
            _context.Clipboard.SetImage(new ImageData(RenderImage()));
            _dirty = false;
            StatusText.Text = "Image copiée dans le presse-papier.";
            RevealButton.IsVisible = false;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _context.Dialogs.ShowWarning("Copie impossible, le presse-papier est occupé par une autre application : " + ex.Message);
        }
    }

    private void ShowSaved(string path)
    {
        _dirty = false;
        StatusText.Text = "Enregistré :";
        RevealButton.Content = path;
        RevealButton.IsVisible = true;
        RevealButton.Click -= RevealSaved;
        RevealButton.Click += RevealSaved;
        RevealButton.Tag = path;
    }

    private void RevealSaved(object? sender, RoutedEventArgs e)
    {
        if (RevealButton.Tag is string path && File.Exists(path))
            _context.Shell.Reveal(path);
    }

    // ---- Keyboard and closing -------------------------------------------------------------------------------

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeText is { IsKeyboardFocusWithin: true })
            return;

        // Undo, delete or a tool change in the middle of a drag would leave the gesture half applied.
        if (_moving is not null || _drawing)
        {
            if (e.Key == Key.Escape)
                CancelGesture();
            e.Handled = true;
            return;
        }

        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(CommandKey))
        {
            var shift = modifiers.HasFlag(KeyModifiers.Shift);
            switch (e.Key)
            {
                case Key.Z when shift: Redo(); break;
                case Key.Z: Undo(); break;
                case Key.Y: Redo(); break;
                case Key.S when shift: _ = SaveAsAsync(); break;
                case Key.S: Save(); break;
                case Key.C: Copy(); break;
                case Key.D0 or Key.NumPad0: ZoomToFit(allowEnlarge: true); break;
                case Key.D1 or Key.NumPad1: SetZoom(1 / _dpiScale); break;
                case Key.Add or Key.OemPlus: SetZoom(_zoom.ScaleX * 1.25); break;
                case Key.Subtract or Key.OemMinus: SetZoom(_zoom.ScaleX / 1.25); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (modifiers != KeyModifiers.None)
            return;
        if (e.Key is Key.Delete or Key.Back)
            DeleteSelected();
        else if (e.Key == Key.Escape)
            Select(null);
        else if (ToolKeys.TryGetValue(e.Key, out var tool))
            SelectTool(tool);
        else
            return;
        e.Handled = true;
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        CommitText();
        if (!_dirty || _closeConfirmed)
            return;
        // The question is asynchronous: cancel this close, then close again once answered.
        e.Cancel = true;
        _ = CloseAsync();
    }

    /// <summary>Closes, after asking whether to save an unsaved capture.</summary>
    /// <returns>False when the user chose to keep the editor open.</returns>
    internal async Task<bool> CloseAsync()
    {
        CommitText();
        if (_dirty && !_closeConfirmed)
        {
            Activate();
            var answer = await _context.Dialogs.AskYesNoCancelAsync(
                "Cette capture n'a été ni enregistrée ni copiée.\n\nL'enregistrer dans le dossier des captures avant de fermer ?");
            if (answer == DialogAnswer.Cancel || (answer == DialogAnswer.Yes && !Save()))
                return false;
        }
        _closeConfirmed = true;
        Close();
        return true;
    }

    private void SavePreferences()
    {
        _settings.EditorColor = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
        _settings.EditorThickness = _thickness;
        _settings.EditorFontSize = _fontSize;
        _context.Store.TrySave(_settings);
        (BaseImage.Source as IDisposable)?.Dispose();
    }

    private static Color ParseColor(string hex) => Color.TryParse(hex, out var color) ? color : Colors.Red;

    private sealed record StepNumber(int Value);
}
