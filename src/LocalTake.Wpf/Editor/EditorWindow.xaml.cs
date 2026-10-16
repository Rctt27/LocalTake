using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LocalTake.Adapters;
using LocalTake.Core.Editor;
using LocalTake.Core.Files;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;
using Path = System.Windows.Shapes.Path;

namespace LocalTake;

/// <summary>What the editor needs from the rest of the app.</summary>
internal sealed record EditorContext(AppSettings Settings, ISettingsStore Store, ImageSaver Saver, IClipboard Clipboard);

/// <summary>
/// Annotation editor. The canvas is sized in image pixels (images are 96 DPI), annotations are ordinary WPF
/// elements positioned on it, and export renders the canvas back at the image's pixel size.
/// </summary>
public partial class EditorWindow : Window
{
    private enum Tool { Select, Arrow, Line, Rectangle, Ellipse, Pen, Highlighter, Text, Number, Blur, Crop }

    private static readonly string[] Palette = { "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF" };
    private static readonly double[] Thicknesses = { 2, 3, 4, 6, 8, 12 };
    private static readonly double[] FontSizes = { 14, 18, 24, 32, 48, 64 };

    private static readonly Dictionary<Key, Tool> ToolKeys = new()
    {
        [Key.V] = Tool.Select,
        [Key.A] = Tool.Arrow,
        [Key.L] = Tool.Line,
        [Key.R] = Tool.Rectangle,
        [Key.E] = Tool.Ellipse,
        [Key.P] = Tool.Pen,
        [Key.H] = Tool.Highlighter,
        [Key.T] = Tool.Text,
        [Key.N] = Tool.Number,
        [Key.B] = Tool.Blur,
        [Key.C] = Tool.Crop
    };

    private readonly AppSettings _settings;
    private readonly EditorContext _context;
    private BitmapSource _base;
    private Tool _tool = Tool.Arrow;
    private Color _color;
    private double _thickness;
    private double _fontSize;
    private double _dpiScale = 1;

    private readonly UndoHistory _history = new();

    /// <summary>A fresh capture counts as unsaved: closing it would lose it.</summary>
    private bool _dirty = true;

    private Point _start;
    private FrameworkElement? _preview;
    private bool _drawing;

    private FrameworkElement? _selected;
    private SelectionAdorner? _selectionAdorner;
    /// <summary>The annotation being dragged with the Select tool.</summary>
    private FrameworkElement? _moving;
    private Point _moveOrigin;
    private Vector _moveStartOffset;

    private TextBox? _activeText;

    // The canvas is in image pixels, and captures are in physical pixels: sizes chosen in the toolbar are
    // scaled like the rest of the screen so that annotations keep the same look on a 150 % or 200 % display.
    private double StrokeWidth => _thickness * _dpiScale;
    private double TextSize => _fontSize * _dpiScale;

    internal EditorWindow(BitmapSource image, EditorContext context)
    {
        InitializeComponent();
        _context = context;
        _settings = context.Settings;
        var settings = _settings;
        _base = image;
        _color = ParseColor(settings.EditorColor);
        _thickness = settings.EditorThickness;
        _fontSize = settings.EditorFontSize;

        Icon = AppIcon.ImageSource;
        Title = $"LocalTake — {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        SetBase(image);
        BuildToolbar();

        Surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
        Surface.MouseMove += Surface_MouseMove;
        Surface.MouseLeftButtonUp += Surface_MouseLeftButtonUp;
        // Alt+Tab or a system dialog in the middle of a drag: no mouse-up will come.
        Surface.LostMouseCapture += (_, _) => CancelGesture();
        Scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
        PreviewKeyDown += Window_PreviewKeyDown;
        SourceInitialized += (_, _) => SizeWindowToImage();
        Loaded += (_, _) => ZoomToFit(allowEnlarge: false);
        DpiChanged += (_, e) =>
        {
            _dpiScale = e.NewDpi.DpiScaleX;
            UpdateZoomText();
        };
        Closing += Window_Closing;

        UpdateUndoButtons();
        StatusText.Text = "Annotez, puis enregistrez (Ctrl+S) ou copiez (Ctrl+C).";
    }

    // ---- Setup ----------------------------------------------------------------------------------------------

    private void BuildToolbar()
    {
        foreach (var radio in Toolbar.Children.OfType<RadioButton>().Where(r => r.GroupName == "Tools"))
            radio.Checked += (s, _) => SelectTool(Enum.Parse<Tool>((string)((RadioButton)s!).Tag));

        foreach (var hex in Palette)
        {
            var color = ParseColor(hex);
            var radio = new RadioButton
            {
                GroupName = "Colors",
                Style = (Style)FindResource("ToolButton"),
                Padding = new Thickness(4),
                ToolTip = hex,
                IsChecked = color == _color,
                Content = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(color), Stroke = Brushes.Gray, StrokeThickness = 1 }
            };
            radio.Checked += (_, _) => _color = color;
            ColorPanel.Children.Add(radio);
        }

        ThicknessBox.ItemsSource = Thicknesses;
        ThicknessBox.SelectedItem = EditorGeometry.Nearest(Thicknesses, _thickness);
        ThicknessBox.SelectionChanged += (_, _) => _thickness = (double)ThicknessBox.SelectedItem;

        FontSizeBox.ItemsSource = FontSizes;
        FontSizeBox.SelectedItem = EditorGeometry.Nearest(FontSizes, _fontSize);
        FontSizeBox.SelectionChanged += (_, _) => _fontSize = (double)FontSizeBox.SelectedItem;
    }

    private void SizeWindowToImage()
    {
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(_base.PixelWidth / _dpiScale + 80, work.Width * 0.9));
        Height = Math.Max(MinHeight, Math.Min(_base.PixelHeight / _dpiScale + 150, work.Height * 0.9));
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }

    private void SetBase(BitmapSource image)
    {
        _base = image;
        BaseImage.Source = image;
        BaseImage.Width = Surface.Width = image.PixelWidth;
        BaseImage.Height = Surface.Height = image.PixelHeight;
        SizeText.Text = $"{image.PixelWidth} × {image.PixelHeight} px";
    }

    // ---- Zoom -----------------------------------------------------------------------------------------------

    private void SetZoom(double zoom)
    {
        zoom = EditorGeometry.ClampZoom(zoom);
        Zoom.ScaleX = Zoom.ScaleY = zoom;
        UpdateZoomText();
    }

    /// <summary>Shown relative to screen pixels, so 100 % means the capture at its real size.</summary>
    private void UpdateZoomText() => ZoomText.Text = $"{Math.Round(Zoom.ScaleX * _dpiScale * 100)} %";

    private void ZoomToFit(bool allowEnlarge)
    {
        var width = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : Scroller.ActualWidth;
        var height = Scroller.ViewportHeight > 0 ? Scroller.ViewportHeight : Scroller.ActualHeight;
        SetZoom(EditorGeometry.FitZoom(width, height, _base.PixelWidth, _base.PixelHeight, _dpiScale, allowEnlarge));
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Zoom.ScaleX * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Zoom.ScaleX / 1.25);
    private void ZoomActual_Click(object sender, RoutedEventArgs e) => SetZoom(1 / _dpiScale);
    private void ZoomFit_Click(object sender, RoutedEventArgs e) => ZoomToFit(allowEnlarge: true);

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;
        SetZoom(Zoom.ScaleX * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    // ---- Tools ----------------------------------------------------------------------------------------------

    private void SelectTool(Tool tool)
    {
        CommitText();
        Select(null);
        _tool = tool;
        Surface.Cursor = tool switch
        {
            Tool.Select => Cursors.Arrow,
            Tool.Text => Cursors.IBeam,
            _ => Cursors.Cross
        };
    }

    private void CheckTool(Tool tool)
    {
        var radio = Toolbar.Children.OfType<RadioButton>().First(r => (string)r.Tag == tool.ToString());
        radio.IsChecked = true;
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeText is not null && IsWithin(e.OriginalSource as DependencyObject, _activeText))
            return;
        CommitText();

        var p = e.GetPosition(Surface);
        _start = p;
        e.Handled = true;

        switch (_tool)
        {
            case Tool.Select:
                var target = FindAnnotation(e.OriginalSource as DependencyObject);
                Select(target);
                if (target is not null)
                {
                    _moving = target;
                    _moveOrigin = p;
                    _moveStartOffset = GetOffset(target);
                    Surface.CaptureMouse();
                }
                break;
            case Tool.Text:
                StartText(p);
                break;
            case Tool.Number:
                AddNumber(p);
                break;
            default:
                _preview = CreateShape(_tool, p);
                Surface.Children.Add(_preview);
                _drawing = true;
                Surface.CaptureMouse();
                break;
        }
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Surface);
        if (_moving is not null)
        {
            SetOffset(_moving, _moveStartOffset + (p - _moveOrigin));
            return;
        }
        if (!_drawing || _preview is null)
            return;

        var constrain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (_tool)
        {
            case Tool.Arrow:
                ((Path)_preview).Data = ArrowGeometry(_start, constrain ? Snap45(_start, p) : p, StrokeWidth);
                break;
            case Tool.Line:
                ((Path)_preview).Data = new LineGeometry(_start, constrain ? Snap45(_start, p) : p);
                break;
            case Tool.Rectangle or Tool.Ellipse or Tool.Blur or Tool.Crop:
                var r = RectBetween(_start, p, square: constrain && _tool is Tool.Rectangle or Tool.Ellipse);
                Canvas.SetLeft(_preview, r.X);
                Canvas.SetTop(_preview, r.Y);
                _preview.Width = r.Width;
                _preview.Height = r.Height;
                break;
            case Tool.Pen or Tool.Highlighter:
                var points = ((Polyline)_preview).Points;
                if (constrain)
                {
                    // Shift draws a straight stroke.
                    while (points.Count > 1)
                        points.RemoveAt(points.Count - 1);
                }
                points.Add(p);
                break;
        }
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_moving is { } element)
        {
            _moving = null;
            Surface.ReleaseMouseCapture();
            var from = _moveStartOffset;
            var to = GetOffset(element);
            if (from != to)
                Push(() => SetOffset(element, from), () => SetOffset(element, to));
            return;
        }
        if (!_drawing || _preview is null)
            return;

        _drawing = false;
        Surface.ReleaseMouseCapture();
        var shape = _preview;
        _preview = null;

        switch (_tool)
        {
            case Tool.Blur:
                Surface.Children.Remove(shape);
                if (ToPixelRect(shape) is { } blurArea)
                    AddAnnotation(CreatePixelated(blurArea));
                break;
            case Tool.Crop:
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
        Surface.ReleaseMouseCapture();
    }

    private FrameworkElement CreateShape(Tool tool, Point p)
    {
        var brush = new SolidColorBrush(_color);
        FrameworkElement shape = tool switch
        {
            Tool.Arrow => new Path { Stroke = brush, Fill = brush, StrokeThickness = StrokeWidth, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Effect = Shadow() },
            Tool.Line => new Path { Stroke = brush, StrokeThickness = StrokeWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Effect = Shadow() },
            Tool.Rectangle => new Rectangle { Stroke = brush, StrokeThickness = StrokeWidth, Effect = Shadow() },
            Tool.Ellipse => new Ellipse { Stroke = brush, StrokeThickness = StrokeWidth, Effect = Shadow() },
            Tool.Pen => new Polyline { Stroke = brush, StrokeThickness = StrokeWidth, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Points = { p } },
            Tool.Highlighter => new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(110, _color.R, _color.G, _color.B)),
                StrokeThickness = Math.Max(14 * _dpiScale, StrokeWidth * 4),
                StrokeLineJoin = PenLineJoin.Round,
                Points = { p }
            },
            _ => new Rectangle
            {
                // Blur and crop: a temporary selection frame.
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
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

    private static bool IsTooSmall(FrameworkElement shape) => shape switch
    {
        Path path => path.Data is null || path.Data.Bounds.IsEmpty || path.Data.Bounds.Width + path.Data.Bounds.Height < 4,
        Polyline line => line.Points.Count < 2,
        _ => shape.Width < 3 || shape.Height < 3
    };

    private static Effect Shadow() => new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1.5, Opacity = 0.45 };

    private static Geometry ArrowGeometry(Point from, Point to, double thickness)
    {
        if (EditorGeometry.Arrow(ToCore(from), ToCore(to), thickness) is not { } arrow)
            return Geometry.Empty;

        var shaft = new PathFigure { StartPoint = ToWpf(arrow.ShaftStart), IsFilled = false };
        shaft.Segments.Add(new LineSegment(ToWpf(arrow.ShaftEnd), true));
        var tip = new PathFigure { StartPoint = ToWpf(arrow.Tip), IsClosed = true, IsFilled = true };
        tip.Segments.Add(new LineSegment(ToWpf(arrow.HeadLeft), true));
        tip.Segments.Add(new LineSegment(ToWpf(arrow.HeadRight), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(shaft);
        geometry.Figures.Add(tip);
        return geometry;
    }

    private static PointD ToCore(Point p) => new(p.X, p.Y);

    private static Point ToWpf(PointD p) => new(p.X, p.Y);

    private static Point Snap45(Point from, Point to) => ToWpf(EditorGeometry.Snap45(ToCore(from), ToCore(to)));

    private static Rect RectBetween(Point a, Point b, bool square)
    {
        var r = EditorGeometry.RectBetween(ToCore(a), ToCore(b), square);
        return new Rect(r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>The frame of a blur/crop gesture as whole image pixels, or null when too small.</summary>
    private Int32Rect? ToPixelRect(FrameworkElement frame)
    {
        var bounds = new RectD(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height);
        return EditorGeometry.ToPixelRect(bounds, _base.PixelWidth, _base.PixelHeight) is { } r
            ? new Int32Rect(r.X, r.Y, r.Width, r.Height)
            : null;
    }

    private FrameworkElement CreatePixelated(Int32Rect area)
    {
        var pixels = new Core.Geometry.PixelRect(area.X, area.Y, area.Width, area.Height);
        var cropped = new CroppedBitmap(_base, area);
        var (columns, rows) = EditorGeometry.PixelationGrid(pixels, EditorGeometry.PixelationBlock(pixels, _dpiScale));
        var small = new TransformedBitmap(cropped, new ScaleTransform((double)columns / area.Width, (double)rows / area.Height));
        small.Freeze();

        var image = new Image { Source = small, Width = area.Width, Height = area.Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, area.X);
        Canvas.SetTop(image, area.Y);
        image.RenderTransform = new TranslateTransform();
        return image;
    }

    private void ApplyCrop(Int32Rect area)
    {
        var oldBase = _base;
        var newBase = new CroppedBitmap(oldBase, area);
        newBase.Freeze();
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
        ZoomToFit(allowEnlarge: false);
    }

    private void AddNumber(Point p)
    {
        // Max + 1 rather than a count: deleting badge 2 of 1-2-3 must not produce a second 3.
        var number = EditorGeometry.NextStepNumber(
            Surface.Children.OfType<FrameworkElement>().Select(c => c.Tag).OfType<StepNumber>().Select(n => n.Value));
        var diameter = Math.Max(24 * _dpiScale, TextSize * 1.25);

        var badge = new Grid { Width = diameter, Height = diameter, Tag = new StepNumber(number), Effect = Shadow() };
        badge.Children.Add(new Ellipse { Fill = new SolidColorBrush(_color), Stroke = Brushes.White, StrokeThickness = Math.Max(1.5, diameter / 16) });
        badge.Children.Add(new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            Foreground = EditorGeometry.NeedsDarkText(_color.R, _color.G, _color.B) ? Brushes.Black : Brushes.White,
            FontWeight = FontWeights.Bold,
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
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(_color),
            CaretBrush = new SolidColorBrush(_color),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderBrush = Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(1),
            AcceptsReturn = true,
            ToolTip = "Entrée : valider · Maj+Entrée : nouvelle ligne"
        };
        Canvas.SetLeft(box, p.X);
        Canvas.SetTop(box, p.Y);
        box.LostKeyboardFocus += (_, _) => CommitText();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
            {
                CommitText();
                e.Handled = true;
            }
        };
        Surface.Children.Add(box);
        _activeText = box;
        Dispatcher.BeginInvoke(() => Keyboard.Focus(box), DispatcherPriority.Input);
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
            FontWeight = FontWeights.Bold,
            Foreground = box.Foreground,
            // Matches the text box border and padding so that the text does not jump.
            Padding = new Thickness(3, 1, 3, 1),
            Effect = Shadow()
        };
        Canvas.SetLeft(block, Canvas.GetLeft(box));
        Canvas.SetTop(block, Canvas.GetTop(box));
        block.RenderTransform = new TranslateTransform();
        AddAnnotation(block);
    }

    // ---- Annotations, selection, undo -----------------------------------------------------------------------

    private IEnumerable<FrameworkElement> Annotations() => Surface.Children.OfType<FrameworkElement>()
        .Where(c => c != BaseImage && c != _preview && c != _activeText);

    private void AddAnnotation(FrameworkElement element)
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
        Push(() => Surface.Children.Insert(Math.Min(index, Surface.Children.Count), element),
            () => Surface.Children.Remove(element));
    }

    private FrameworkElement? FindAnnotation(DependencyObject? source)
    {
        while (source is not null)
        {
            var parent = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            if (parent == Surface)
                return source == BaseImage ? null : source as FrameworkElement;
            source = parent;
        }
        return null;
    }

    private static bool IsWithin(DependencyObject? source, DependencyObject ancestor)
    {
        while (source is not null)
        {
            if (source == ancestor)
                return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void Select(FrameworkElement? element)
    {
        if (_selectionAdorner is not null)
        {
            AdornerLayer.GetAdornerLayer(Surface)?.Remove(_selectionAdorner);
            _selectionAdorner = null;
        }
        _selected = element;
        if (element is not null && AdornerLayer.GetAdornerLayer(Surface) is { } layer)
        {
            _selectionAdorner = new SelectionAdorner(element);
            layer.Add(_selectionAdorner);
        }
    }

    private static Vector GetOffset(UIElement element) =>
        element.RenderTransform is TranslateTransform t ? new Vector(t.X, t.Y) : default;

    private void SetOffset(UIElement element, Vector offset)
    {
        if (element.RenderTransform is not TranslateTransform t)
            element.RenderTransform = t = new TranslateTransform();
        t.X = offset.X;
        t.Y = offset.Y;
        if (element == _selected)
            AdornerLayer.GetAdornerLayer(Surface)?.Update(element);
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

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    // ---- Output ---------------------------------------------------------------------------------------------

    /// <summary>The capture with its annotations, at the image's pixel size.</summary>
    private BitmapSource RenderImage()
    {
        CommitText();
        Surface.UpdateLayout();
        var width = _base.PixelWidth;
        var height = _base.PixelHeight;
        var bounds = new Rect(0, 0, width, height);

        // Through a brush, the canvas renders in its own coordinates whatever the zoom and scroll position.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(Surface)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = bounds,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = bounds
            }, null, bounds);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private bool Save()
    {
        try
        {
            ShowSaved(_context.Saver.SaveToOutputFolder(new WpfImage(RenderImage())));
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Enregistrement impossible : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SaveAs()
    {
        try
        {
            if (_context.Saver.SaveAs(new WpfImage(RenderImage()), new WpfSaveFileDialog(this)) is { } path)
                ShowSaved(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Enregistrement impossible : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Copy()
    {
        try
        {
            _context.Clipboard.SetImage(new WpfImage(RenderImage()));
            _dirty = false;
            StatusText.Text = "Image copiée dans le presse-papier.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Copie impossible, le presse-papier est occupé par une autre application : " + ex.Message,
                "LocalTake", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowSaved(string path)
    {
        _dirty = false;
        var link = new Hyperlink(new Run(path)) { Foreground = new SolidColorBrush(Color.FromRgb(108, 182, 255)) };
        link.Click += (_, _) => Process.Start("explorer.exe", $"/select,\"{path}\"");
        StatusText.Inlines.Clear();
        StatusText.Inlines.Add(new Run("Enregistré : "));
        StatusText.Inlines.Add(link);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();
    private void Copy_Click(object sender, RoutedEventArgs e) => Copy();

    // ---- Keyboard and closing -------------------------------------------------------------------------------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_activeText is { IsKeyboardFocusWithin: true })
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Undo, delete or a tool change in the middle of a drag would leave the gesture half applied.
        if (_moving is not null || _drawing)
        {
            if (key == Key.Escape)
                CancelGesture();
            e.Handled = true;
            return;
        }
        var modifiers = Keyboard.Modifiers;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            var shift = modifiers.HasFlag(ModifierKeys.Shift);
            switch (key)
            {
                case Key.Z when shift: Redo(); break;
                case Key.Z: Undo(); break;
                case Key.Y: Redo(); break;
                case Key.S when shift: SaveAs(); break;
                case Key.S: Save(); break;
                case Key.C: Copy(); break;
                case Key.D0 or Key.NumPad0: ZoomToFit(allowEnlarge: true); break;
                case Key.D1 or Key.NumPad1: SetZoom(1 / _dpiScale); break;
                case Key.Add or Key.OemPlus: SetZoom(Zoom.ScaleX * 1.25); break;
                case Key.Subtract or Key.OemMinus: SetZoom(Zoom.ScaleX / 1.25); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (modifiers != ModifierKeys.None)
            return;

        if (key is Key.Delete or Key.Back)
        {
            DeleteSelected();
        }
        else if (key == Key.Escape)
        {
            Select(null);
        }
        else if (ToolKeys.TryGetValue(key, out var tool))
        {
            CheckTool(tool);
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CommitText();
        if (_dirty)
        {
            var answer = MessageBox.Show(this,
                "Cette capture n'a été ni enregistrée ni copiée.\n\nL'enregistrer dans le dossier des captures avant de fermer ?",
                "LocalTake", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save()))
            {
                e.Cancel = true;
                return;
            }
        }

        _settings.EditorColor = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
        _settings.EditorThickness = _thickness;
        _settings.EditorFontSize = _fontSize;
        _context.Store.TrySave(_settings);
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Red;
        }
    }

    private sealed record StepNumber(int Value);

    /// <summary>Dashed frame around the selected annotation; adorners are not part of the exported image.</summary>
    private sealed class SelectionAdorner : Adorner
    {
        private static readonly Pen FramePen = CreatePen();

        public SelectionAdorner(UIElement element) : base(element) => IsHitTestVisible = false;

        private static Pen CreatePen()
        {
            var pen = new Pen(Brushes.DeepSkyBlue, 1.5) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = VisualTreeHelper.GetDescendantBounds(AdornedElement);
            if (bounds.IsEmpty)
                bounds = new Rect(AdornedElement.RenderSize);
            bounds.Inflate(4, 4);
            dc.DrawRectangle(null, FramePen, bounds);
        }
    }
}
