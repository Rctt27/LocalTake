using System.Globalization;
using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using LocalTake.Core.Editor;
using LocalTake.Core.Files;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;
using LocalTake.Mac.Adapters;

namespace LocalTake.Mac.Editor;

/// <summary>What the editor needs from the rest of the app.</summary>
internal sealed record EditorContext(AppSettings Settings, ISettingsStore Store, ImageSaver Saver, IClipboard Clipboard, IShell Shell);

/// <summary>
/// The annotation editor's window: toolbar, the image in a zooming scroll view, and a status line. Saving, copying
/// and zooming answer the main menu (⌘S, ⇧⌘S, ⌘0, ⌘1…) through the responder chain.
/// </summary>
internal sealed class EditorWindowController : NSWindowController
{
    private readonly MacImage _image;
    private readonly EditorContext _context;
    private readonly AnnotationView _view;
    private readonly EditorToolbar _toolbar;
    private readonly NSScrollView _scroll;
    private readonly NSTextField _sizeText;
    private readonly NSTextField _zoomText;
    private readonly NSButton _statusText;
    private NSObject? _magnifyObserver;
    private string? _savedPath;

    /// <summary>A fresh capture counts as unsaved: closing it would lose it.</summary>
    private bool _dirty = true;

    public EditorWindowController(MacImage image, EditorContext context) : base(CreateWindow(image))
    {
        _image = image;
        _context = context;
        var settings = context.Settings;
        var window = Window!;

        _view = new AnnotationView(image.Image)
        {
            Color = Colors.Parse(settings.EditorColor),
            Thickness = settings.EditorThickness,
            FontSize = settings.EditorFontSize,
            DpiScale = window.BackingScaleFactor
        };
        _toolbar = new EditorToolbar(_view.Color, _view.Thickness, _view.FontSize) { Tool = _view.Tool };
        _scroll = new NSScrollView
        {
            HasHorizontalScroller = true,
            HasVerticalScroller = true,
            AllowsMagnification = true,
            MinMagnification = (NFloat)EditorGeometry.MinZoom,
            MaxMagnification = (NFloat)EditorGeometry.MaxZoom,
            DrawsBackground = true,
            BackgroundColor = NSColor.FromSrgb(30f / 255, 30f / 255, 30f / 255, 1f),
            ContentView = new CenteringClipView(),
            DocumentView = _view
        };

        _statusText = NSButton.CreateButton("Annotez, puis enregistrez (⌘S) ou copiez (⌘C).", () =>
        {
            if (_savedPath is { } path)
                _context.Shell.Reveal(path);
        });
        _statusText.Bordered = false;
        _statusText.ToolTip = "Cliquez pour afficher le fichier enregistré dans le Finder.";
        _statusText.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
        // The status takes the room left, and a long path is shortened rather than pushing the zoom away.
        _statusText.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        _statusText.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Horizontal);
        _sizeText = NSTextField.CreateLabel("");
        _zoomText = NSTextField.CreateLabel("");
        var zoomButtons = new NSView[]
        {
            NSButton.CreateButton("−", () => SetZoom(_scroll.Magnification / 1.25)),
            _zoomText,
            NSButton.CreateButton("+", () => SetZoom(_scroll.Magnification * 1.25)),
            NSButton.CreateButton("1:1", () => SetZoom(1 / _view.DpiScale)),
            NSButton.CreateButton("Ajuster", () => ZoomToFit(allowEnlarge: true))
        };
        var status = NSStackView.FromViews([_statusText, _sizeText, .. zoomButtons]);
        status.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        foreach (var control in status.ArrangedSubviews.OfType<NSControl>())
        {
            control.ControlSize = NSControlSize.Small;
            control.RefusesFirstResponder = true;
        }

        Layout(window.ContentView!, _toolbar.View, _scroll, status);
        Wire(window);
        UpdateSize();
        UpdateUndo();
    }

    /// <summary>The window has closed; the launcher forgets it.</summary>
    public event EventHandler? Closed;

    private static NSWindow CreateWindow(MacImage image)
    {
        var screen = NSScreen.MainScreen;
        var visible = screen?.VisibleFrame ?? new CGRect(0, 0, 1440, 900);
        var scale = (double)(screen?.BackingScaleFactor ?? 2);
        // The image at its real size if it fits, with room for the toolbar and the status line.
        var width = Math.Max(960, Math.Min(image.Width / scale + 80, visible.Width * 0.9));
        var height = Math.Max(420, Math.Min(image.Height / scale + 150, visible.Height * 0.9));
        var window = new NSWindow(new CGRect(0, 0, width, height),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered, false)
        {
            Title = string.Create(CultureInfo.InvariantCulture, $"LocalTake — {DateTime.Now:yyyy-MM-dd HH:mm:ss}"),
            ContentMinSize = new CGSize(960, 420),
            // The same dark editor as on Windows.
            Appearance = NSAppearance.GetAppearance(NSAppearance.NameDarkAqua)
        };
        window.ReleaseWhenClosed(false);
        window.Center();
        return window;
    }

    private static void Layout(NSView content, NSView toolbar, NSView scroll, NSView status)
    {
        foreach (var view in new[] { toolbar, scroll, status })
        {
            view.TranslatesAutoresizingMaskIntoConstraints = false;
            content.AddSubview(view);
        }
        NSLayoutConstraint.ActivateConstraints(
        [
            toolbar.TopAnchor.ConstraintEqualTo(content.TopAnchor, 8),
            toolbar.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 10),
            toolbar.TrailingAnchor.ConstraintLessThanOrEqualTo(content.TrailingAnchor, -10),
            scroll.TopAnchor.ConstraintEqualTo(toolbar.BottomAnchor, 8),
            scroll.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor),
            status.TopAnchor.ConstraintEqualTo(scroll.BottomAnchor, 4),
            status.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 10),
            status.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor, -10),
            status.BottomAnchor.ConstraintEqualTo(content.BottomAnchor, -6)
        ]);
    }

    private void Wire(NSWindow window)
    {
        _toolbar.ToolChanged += (_, _) => _view.Tool = _toolbar.Tool;
        _toolbar.ColorChanged += (_, color) => _view.Color = color;
        _toolbar.ThicknessChanged += (_, value) => _view.Thickness = value;
        _toolbar.FontSizeChanged += (_, value) => _view.FontSize = value;
        _toolbar.UndoClicked += (_, _) => _view.Undo(null);
        _toolbar.RedoClicked += (_, _) => _view.Redo(null);
        _toolbar.CopyClicked += (_, _) => Copy();
        _toolbar.SaveAsClicked += (_, _) => SaveAs();
        _toolbar.SaveClicked += (_, _) => Save();

        _view.ToolChanged += (_, _) => _toolbar.Tool = _view.Tool;
        _view.CopyRequested += (_, _) => Copy();
        _view.Changed += (_, _) =>
        {
            _dirty = true;
            UpdateUndo();
        };
        _view.ImageResized += (_, _) =>
        {
            UpdateSize();
            ZoomToFit(allowEnlarge: false);
        };

        _magnifyObserver = NSNotificationCenter.DefaultCenter.AddObserver(NSScrollView.DidEndLiveMagnifyNotification, _ => UpdateZoomText(), _scroll);
        window.DidChangeBackingProperties += (_, _) =>
        {
            _view.DpiScale = window.BackingScaleFactor;
            UpdateZoomText();
        };
        window.WindowShouldClose = _ => ConfirmClose();
        window.WillClose += (_, _) => OnClosed();
    }

    public void Show()
    {
        NSApplication.SharedApplication.Activate();
        ShowWindow(this);
        Window!.MakeKeyAndOrderFront(this);
        Window.MakeFirstResponder(_view);
        ZoomToFit(allowEnlarge: false);
    }

    /// <summary>Closes the window unless the user cancels the question about the unsaved capture.</summary>
    public bool TryClose()
    {
        if (!ConfirmClose())
            return false;
        Window!.Close();
        return true;
    }

    // ---- Zoom -----------------------------------------------------------------------------------------------

    private void SetZoom(double zoom)
    {
        _scroll.Magnification = (NFloat)EditorGeometry.ClampZoom(zoom);
        UpdateZoomText();
    }

    private void ZoomToFit(bool allowEnlarge)
    {
        var viewport = _scroll.ContentSize;
        SetZoom(EditorGeometry.FitZoom(viewport.Width, viewport.Height, _view.ImageWidth, _view.ImageHeight, _view.DpiScale, allowEnlarge));
    }

    /// <summary>Shown relative to screen pixels, so 100 % means the capture at its real size.</summary>
    private void UpdateZoomText() =>
        _zoomText.StringValue = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(_scroll.Magnification * _view.DpiScale * 100)} %");

    [Export("zoomIn:")]
    public void ZoomIn(NSObject? sender) => SetZoom(_scroll.Magnification * 1.25);

    [Export("zoomOut:")]
    public void ZoomOut(NSObject? sender) => SetZoom(_scroll.Magnification / 1.25);

    [Export("actualSize:")]
    public void ActualSize(NSObject? sender) => SetZoom(1 / _view.DpiScale);

    [Export("fitToWindow:")]
    public void FitToWindow(NSObject? sender) => ZoomToFit(allowEnlarge: true);

    // ---- Output ---------------------------------------------------------------------------------------------

    [Export("saveDocument:")]
    public void SaveDocument(NSObject? sender) => Save();

    [Export("saveDocumentAs:")]
    public void SaveDocumentAs(NSObject? sender) => SaveAs();

    private bool Save()
    {
        try
        {
            using var image = MacImage.FromPixels(_view.RenderImage());
            ShowSaved(_context.Saver.SaveToOutputFolder(image));
            return true;
        }
#pragma warning disable CA1031 // Whatever failed, the capture stays open and the user is told why.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MacDialogs.Show("Enregistrement impossible : " + ex.Message, NSAlertStyle.Critical);
            return false;
        }
    }

    private async void SaveAs()
    {
        try
        {
            using var image = MacImage.FromPixels(_view.RenderImage());
            if (await _context.Saver.SaveAsAsync(image, new MacSaveFileDialog()).ConfigureAwait(true) is { } path)
                ShowSaved(path);
        }
#pragma warning disable CA1031 // Whatever failed, the capture stays open and the user is told why.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MacDialogs.Show("Enregistrement impossible : " + ex.Message, NSAlertStyle.Critical);
        }
    }

    private void Copy()
    {
        try
        {
            using var image = MacImage.FromPixels(_view.RenderImage());
            _context.Clipboard.SetImage(image);
            _dirty = false;
            SetStatus("Image copiée dans le presse-papiers.", path: null);
        }
#pragma warning disable CA1031 // Whatever failed, the capture stays open and the user is told why.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MacDialogs.Show("Copie impossible : " + ex.Message, NSAlertStyle.Warning);
        }
    }

    private void ShowSaved(string path)
    {
        _dirty = false;
        SetStatus("Enregistré : " + path, path);
    }

    private void SetStatus(string text, string? path)
    {
        _savedPath = path;
        _statusText.Title = text;
    }

    private void UpdateSize() =>
        _sizeText.StringValue = string.Create(CultureInfo.InvariantCulture, $"{_view.ImageWidth} × {_view.ImageHeight} px");

    private void UpdateUndo() => _toolbar.SetUndoState(_view.CanUndo, _view.CanRedo);

    // ---- Closing --------------------------------------------------------------------------------------------

    private bool ConfirmClose()
    {
        _view.CommitText();
        if (!_dirty)
            return true;
        var answer = MacDialogs.AskYesNoCancel(
            "Cette capture n'a été ni enregistrée ni copiée.\n\nL'enregistrer dans le dossier des captures avant de fermer ?");
        return answer == DialogAnswer.No || (answer == DialogAnswer.Yes && Save());
    }

    private void OnClosed()
    {
        var settings = _context.Settings;
        settings.EditorColor = Colors.ToHex(_view.Color);
        settings.EditorThickness = _view.Thickness;
        settings.EditorFontSize = _view.FontSize;
        _context.Store.TrySave(settings);
        if (_magnifyObserver is not null)
            NSNotificationCenter.DefaultCenter.RemoveObserver(_magnifyObserver);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _view.Dispose();
            _image.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Keeps an image smaller than the window in its centre, rather than in a corner.</summary>
    private sealed class CenteringClipView : NSClipView
    {
        public override CGRect ConstrainBoundsRect(CGRect proposedBounds)
        {
            var rect = base.ConstrainBoundsRect(proposedBounds);
            if (DocumentView is not { } document)
                return rect;
            var size = document.Frame.Size;
            var x = rect.Width > size.Width ? (size.Width - rect.Width) / 2 : rect.X;
            var y = rect.Height > size.Height ? (size.Height - rect.Height) / 2 : rect.Y;
            return new CGRect(x, y, rect.Width, rect.Height);
        }
    }
}

/// <summary>Opens each capture in its own editor window, and closes them all when LocalTake quits.</summary>
internal sealed class MacEditorLauncher(EditorContext context) : IEditorLauncher
{
    private readonly List<EditorWindowController> _open = [];

    public void Open(IImage image)
    {
        var editor = new EditorWindowController(image as MacImage ?? throw new ArgumentException("Not a macOS image.", nameof(image)), context);
        _open.Add(editor);
        editor.Closed += (_, _) =>
        {
            _open.Remove(editor);
            // Released once AppKit has finished closing the window.
            CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(editor.Dispose);
        };
        editor.Show();
    }

    /// <summary>Asks about each unsaved capture; false when the user cancelled one of the questions.</summary>
    public bool CloseAll()
    {
        foreach (var editor in _open.ToList())
            if (!editor.TryClose())
                return false;
        return true;
    }
}
