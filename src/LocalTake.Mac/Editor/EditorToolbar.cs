using System.Globalization;
using AppKit;
using CoreGraphics;
using LocalTake.Core.Editor;

namespace LocalTake.Mac.Editor;

/// <summary>The editor's tools, colours, sizes and actions, in one row, as on Windows.</summary>
internal sealed class EditorToolbar : IDisposable
{
    private static readonly string[] Palette = ["#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#000000", "#FFFFFF"];
    private static readonly double[] Thicknesses = [2, 3, 4, 6, 8, 12];
    private static readonly double[] FontSizes = [14, 18, 24, 32, 48, 64];

    private static readonly (Tool Tool, string Symbol, string Fallback, string Tip)[] Tools =
    [
        (Tool.Select, "cursorarrow", "↖", "Sélection : déplacer, Suppr pour effacer (V)"),
        (Tool.Arrow, "arrow.up.right", "➚", "Flèche (A)"),
        (Tool.Line, "line.diagonal", "╱", "Ligne (L)"),
        (Tool.Rectangle, "rectangle", "▭", "Rectangle (R)"),
        (Tool.Ellipse, "circle", "◯", "Ellipse (E)"),
        (Tool.Pen, "pencil", "✎", "Crayon (P)"),
        (Tool.Highlighter, "highlighter", "▌", "Surligneur (H)"),
        (Tool.Text, "textformat", "T", "Texte (T)"),
        (Tool.Number, "1.circle", "①", "Numérotation (N)"),
        (Tool.Blur, "square.grid.3x3", "▦", "Flouter / pixeliser (B)"),
        (Tool.Crop, "crop", "⛶", "Recadrer (C)")
    ];

    private readonly NSSegmentedControl _tools;
    private readonly NSButton _undo;
    private readonly NSButton _redo;

    public EditorToolbar(NSColor color, double thickness, double fontSize)
    {
        _tools = new NSSegmentedControl { SegmentCount = Tools.Length, TrackingMode = NSSegmentSwitchTracking.SelectOne };
        for (var i = 0; i < Tools.Length; i++)
        {
            var (_, symbol, fallback, tip) = Tools[i];
            if (NSImage.GetSystemSymbol(symbol, tip) is { } image)
                _tools.SetImage(image, i);
            else
                _tools.SetLabel(fallback, i);
            _tools.SetToolTip(tip, i);
        }
        _tools.Activated += (_, _) => ToolChanged?.Invoke(this, EventArgs.Empty);

        var colours = new NSSegmentedControl { SegmentCount = Palette.Length, TrackingMode = NSSegmentSwitchTracking.SelectOne };
        var current = Colors.ToHex(color);
        for (var i = 0; i < Palette.Length; i++)
        {
            colours.SetImage(Swatch(Colors.Parse(Palette[i])), i);
            colours.SetToolTip(Palette[i], i);
            if (string.Equals(Palette[i], current, StringComparison.OrdinalIgnoreCase))
                colours.SelectedSegment = i;
        }
        colours.Activated += (_, _) => ColorChanged?.Invoke(this, Colors.Parse(Palette[(int)colours.SelectedSegment]));

        var thicknessBox = Choice(Thicknesses, thickness, value => ThicknessChanged?.Invoke(this, value));
        var fontSizeBox = Choice(FontSizes, fontSize, value => FontSizeChanged?.Invoke(this, value));

        _undo = SymbolButton("arrow.uturn.backward", "↶", "Annuler (⌘Z)", () => UndoClicked?.Invoke(this, EventArgs.Empty));
        _redo = SymbolButton("arrow.uturn.forward", "↷", "Rétablir (⇧⌘Z)", () => RedoClicked?.Invoke(this, EventArgs.Empty));
        var copy = TextButton("Copier", "Copier l'image (⌘C)", () => CopyClicked?.Invoke(this, EventArgs.Empty));
        var saveAs = TextButton("Enregistrer sous…", "Enregistrer sous… (⇧⌘S)", () => SaveAsClicked?.Invoke(this, EventArgs.Empty));
        var save = TextButton("Enregistrer", "Enregistrer dans le dossier des captures (⌘S)", () => SaveClicked?.Invoke(this, EventArgs.Empty));

        View = NSStackView.FromViews(
        [
            _tools, colours,
            Label("Épaisseur"), thicknessBox, Label("Texte"), fontSizeBox,
            _undo, _redo,
            copy, saveAs, save
        ]);
        View.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        View.Spacing = 6;
        // Groups: tools, colours, sizes, history, output.
        foreach (var last in new NSView[] { _tools, colours, fontSizeBox, _redo })
            View.SetCustomSpacing(18, last);
        foreach (var control in View.ArrangedSubviews.OfType<NSControl>())
        {
            control.ControlSize = NSControlSize.Small;
            // Clicks on the toolbar leave the keyboard to the image, for the tool keys and ⌘Z.
            control.RefusesFirstResponder = true;
        }
    }

    public event EventHandler? ToolChanged;
    public event EventHandler<NSColor>? ColorChanged;
    public event EventHandler<double>? ThicknessChanged;
    public event EventHandler<double>? FontSizeChanged;
    public event EventHandler? UndoClicked;
    public event EventHandler? RedoClicked;
    public event EventHandler? CopyClicked;
    public event EventHandler? SaveAsClicked;
    public event EventHandler? SaveClicked;

    public NSStackView View { get; }

    public Tool Tool
    {
        get => Tools[(int)Math.Max(0, _tools.SelectedSegment)].Tool;
        set => _tools.SelectedSegment = Array.FindIndex(Tools, t => t.Tool == value);
    }

    public void Dispose()
    {
        _tools.Dispose();
        _undo.Dispose();
        _redo.Dispose();
        View.Dispose();
    }

    public void SetUndoState(bool canUndo, bool canRedo)
    {
        _undo.Enabled = canUndo;
        _redo.Enabled = canRedo;
    }

    private static NSPopUpButton Choice(double[] options, double value, Action<double> changed)
    {
        var box = new NSPopUpButton(CGRect.Empty, false);
        box.AddItems([.. options.Select(o => o.ToString(CultureInfo.InvariantCulture))]);
        box.SelectItem(Array.IndexOf(options, EditorGeometry.Nearest(options, value)));
        box.Activated += (_, _) => changed(options[(int)box.IndexOfSelectedItem]);
        return box;
    }

    private static NSButton SymbolButton(string symbol, string fallback, string tip, Action action)
    {
        var button = NSImage.GetSystemSymbol(symbol, tip) is { } image
            ? NSButton.CreateButton(image, action)
            : NSButton.CreateButton(fallback, action);
        button.ToolTip = tip;
        return button;
    }

    private static NSButton TextButton(string title, string tip, Action action)
    {
        var button = NSButton.CreateButton(title, action);
        button.ToolTip = tip;
        return button;
    }

    private static NSTextField Label(string text)
    {
        var label = NSTextField.CreateLabel(text);
        label.TextColor = NSColor.SecondaryLabel;
        return label;
    }

    /// <summary>A colour dot for the palette.</summary>
    private static NSImage Swatch(NSColor color) => NSImage.ImageWithSize(new CGSize(16, 16), false, rect =>
    {
        var dot = NSBezierPath.FromOvalInRect(rect.Inset(1.5f, 1.5f));
        color.SetFill();
        dot.Fill();
        NSColor.Gray.SetStroke();
        dot.LineWidth = 1;
        dot.Stroke();
        return true;
    });
}
