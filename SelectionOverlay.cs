using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace LocalTake;

/// <summary>
/// Full-screen overlay over a frozen screenshot: drag to select a region, click to take the window under the
/// cursor, Space to switch to window mode, Escape or right-click to cancel.
/// </summary>
internal sealed class SelectionOverlay : Form
{
    private static readonly Color Accent = Color.FromArgb(255, 87, 34);

    private readonly Bitmap _screen;
    private readonly Rectangle _virtualScreen;
    private readonly List<Rectangle> _windows = new();
    private bool _windowMode;

    private Point? _dragStart;
    private Rectangle _selection;
    private Rectangle? _hoverWindow;
    private Point _cursor = new(-1, -1);

    private readonly SolidBrush _dimBrush = new(Color.FromArgb(120, 0, 0, 0));
    private readonly Pen _accentPen = new(Accent, 2);
    private readonly Pen _crosshairPen = new(Color.FromArgb(140, 255, 255, 255)) { DashStyle = DashStyle.Dash };
    private readonly SolidBrush _labelBrush = new(Accent);
    private readonly Font _labelFont = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _hintFont = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>Selected area in virtual-screen coordinates (physical pixels).</summary>
    public Rectangle? Result { get; private set; }

    public SelectionOverlay(Bitmap screen, Rectangle virtualScreen, IEnumerable<Rectangle> windows, bool windowMode)
    {
        // A premultiplied copy draws several times faster, which keeps the overlay fluid on large screens.
        _screen = screen.Clone(new Rectangle(0, 0, screen.Width, screen.Height), PixelFormat.Format32bppPArgb);
        _virtualScreen = virtualScreen;
        _windowMode = windowMode;
        foreach (var w in windows)
        {
            // Window rectangles converted to overlay (client) coordinates.
            var local = Rectangle.Intersect(w, virtualScreen);
            local.Offset(-virtualScreen.X, -virtualScreen.Y);
            if (local.Width > 1 && local.Height > 1)
                _windows.Add(local);
        }

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = virtualScreen;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _virtualScreen;
        Activate();
        Native.SetForegroundWindow(Handle);
        _cursor = PointToClient(Cursor.Position);
        _hoverWindow = WindowAt(_cursor);
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        // The form spans every monitor: a DPI change must not let WinForms resize it.
        if (m.Msg == Native.WM_DPICHANGED)
            return;
        base.WndProc(ref m);
    }

    private Rectangle? WindowAt(Point p)
    {
        foreach (var w in _windows)
            if (w.Contains(p))
                return w;
        return null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
        }
        else if (e.KeyCode == Keys.Space && _dragStart is null)
        {
            _windowMode = !_windowMode;
            Invalidate();
        }
        else if (e.KeyCode == Keys.Enter && _hoverWindow is { } window)
        {
            Accept(window);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            if (_dragStart is null)
                DialogResult = DialogResult.Cancel;
            _dragStart = null;
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            _dragStart = e.Location;
            _selection = Rectangle.Empty;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _cursor = e.Location;
        if (_dragStart is { } start && !_windowMode)
        {
            _selection = Rectangle.FromLTRB(
                Math.Min(start.X, e.X), Math.Min(start.Y, e.Y),
                Math.Max(start.X, e.X) + 1, Math.Max(start.Y, e.Y) + 1);
        }
        else
        {
            _hoverWindow = WindowAt(e.Location);
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _dragStart is null)
            return;
        _dragStart = null;

        if (!_windowMode && _selection.Width >= 3 && _selection.Height >= 3)
            Accept(_selection);
        else if (WindowAt(e.Location) is { } window)
            Accept(window);
        else
            Invalidate();
    }

    private void Accept(Rectangle local)
    {
        local.Offset(_virtualScreen.X, _virtualScreen.Y);
        Result = local;
        DialogResult = DialogResult.OK;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is painted in OnPaint; skipping the background avoids flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(_screen, 0, 0);
        g.CompositingMode = CompositingMode.SourceOver;

        Rectangle? highlight = _dragStart is not null && !_windowMode && _selection.Width > 0
            ? _selection
            : _hoverWindow;

        if (highlight is { } rect)
        {
            g.SetClip(rect, CombineMode.Exclude);
            g.FillRectangle(_dimBrush, ClientRectangle);
            g.ResetClip();
            g.DrawRectangle(_accentPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            DrawSizeLabel(g, rect);
        }
        else
        {
            g.FillRectangle(_dimBrush, ClientRectangle);
        }

        if (!_windowMode && _dragStart is null && _cursor.X >= 0)
        {
            g.DrawLine(_crosshairPen, _cursor.X, 0, _cursor.X, Height);
            g.DrawLine(_crosshairPen, 0, _cursor.Y, Width, _cursor.Y);
        }

        DrawHint(g);
    }

    private void DrawSizeLabel(Graphics g, Rectangle rect)
    {
        var text = $"{rect.Width} × {rect.Height}";
        var size = g.MeasureString(text, _labelFont);
        var x = rect.X;
        var y = rect.Y - size.Height - 6;
        if (y < 0)
            y = rect.Y + 6;
        var box = new RectangleF(x, y, size.Width + 10, size.Height + 2);
        g.FillRectangle(_labelBrush, box);
        g.DrawString(text, _labelFont, Brushes.White, box.X + 5, box.Y + 1);
    }

    private void DrawHint(Graphics g)
    {
        var text = _windowMode
            ? "Mode fenêtre : cliquez sur une fenêtre   ·   Espace : mode zone   ·   Échap : annuler"
            : "Glissez pour sélectionner une zone   ·   Clic : fenêtre sous le curseur   ·   Espace : mode fenêtre   ·   Échap : annuler";

        // Centered at the top of the monitor under the cursor.
        var screen = Screen.FromPoint(new Point(_cursor.X + _virtualScreen.X, _cursor.Y + _virtualScreen.Y)).Bounds;
        screen.Offset(-_virtualScreen.X, -_virtualScreen.Y);
        var size = g.MeasureString(text, _hintFont);
        var box = new RectangleF(screen.X + (screen.Width - size.Width) / 2 - 12, screen.Y + 16, size.Width + 24, size.Height + 12);
        using var background = new SolidBrush(Color.FromArgb(210, 30, 30, 30));
        g.FillRectangle(background, box);
        g.DrawString(text, _hintFont, Brushes.White, box.X + 12, box.Y + 6);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screen.Dispose();
            _dimBrush.Dispose();
            _accentPen.Dispose();
            _crosshairPen.Dispose();
            _labelBrush.Dispose();
            _labelFont.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
