using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ShowTime.Adapters;
using ShowTime.Core.Capture;
using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;

namespace ShowTime;

/// <summary>
/// Full-screen overlay over a frozen screenshot. The rules (drag, click on a window, Space, Escape, right-click) are
/// <see cref="SelectionModel"/>'s; this form draws its state and reports the input.
/// </summary>
internal sealed class SelectionOverlay : Form
{
    private static readonly Color Accent = Color.FromArgb(255, 87, 34);

    private readonly Bitmap _screen;
    private readonly Rectangle _virtualScreen;
    private readonly SelectionModel _model;

    private readonly SolidBrush _dimBrush = new(Color.FromArgb(120, 0, 0, 0));
    private readonly Pen _accentPen = new(Accent, 2);
    private readonly Pen _crosshairPen = new(Color.FromArgb(140, 255, 255, 255)) { DashStyle = DashStyle.Dash };
    private readonly SolidBrush _labelBrush = new(Accent);
    private readonly Font _labelFont = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _hintFont = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>Selected area in virtual-screen coordinates (physical pixels).</summary>
    public Rectangle? Result { get; private set; }

    /// <summary>Lets the user pick an area of the frozen screen. Shared by image captures and videos.</summary>
    /// <param name="screen">The frozen virtual desktop.</param>
    /// <param name="virtualScreen">Where it lies, in physical pixels.</param>
    /// <param name="windows">The visible windows, front to back, for window mode.</param>
    /// <param name="windowMode">Start in window mode rather than area mode.</param>
    /// <returns>The chosen area in virtual-screen coordinates (physical pixels), or null when the user cancelled.</returns>
    public static PixelRect? SelectArea(PixelBuffer screen, PixelRect virtualScreen, IReadOnlyList<PixelRect> windows, bool windowMode)
    {
        using var overlay = new SelectionOverlay(ToBitmap(screen), virtualScreen, windows, windowMode);
        return overlay.ShowDialog() == DialogResult.OK && overlay.Result is { } selected ? selected.ToPixelRect() : null;
    }

    /// <summary>
    /// A premultiplied bitmap draws several times faster, which keeps the overlay fluid on large screens; captures
    /// are opaque, so their pixels are the same premultiplied or not.
    /// </summary>
    private static Bitmap ToBitmap(PixelBuffer pixels)
    {
        var bitmap = new Bitmap(pixels.Width, pixels.Height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, pixels.Width, pixels.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var y = 0; y < pixels.Height; y++)
                Marshal.Copy(pixels.Pixels, y * pixels.Stride, data.Scan0 + y * data.Stride, pixels.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private SelectionOverlay(Bitmap screen, PixelRect virtualScreen, IEnumerable<PixelRect> windows, bool windowMode)
    {
        _screen = screen;
        _virtualScreen = virtualScreen.ToRectangle();
        _model = new SelectionModel(SelectionModel.ToLocal(windows, virtualScreen), windowMode);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualScreen;
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
        var cursor = PointToClient(Cursor.Position);
        _model.PointerMoved(new PixelPoint(cursor.X, cursor.Y));
        Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        // The form spans every monitor: a DPI change must not let WinForms resize it.
        if (m.Msg == Native.WM_DPICHANGED)
            return;
        base.WndProc(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        SelectionKey? key = e.KeyCode switch
        {
            Keys.Escape => SelectionKey.Escape,
            Keys.Space => SelectionKey.Space,
            Keys.Enter => SelectionKey.Enter,
            _ => null
        };
        if (key is { } pressed)
            Apply(() => _model.KeyPressed(pressed));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            Apply(_model.RightPressed);
        else if (e.Button == MouseButtons.Left)
            Apply(() => _model.LeftPressed(new PixelPoint(e.X, e.Y)));
    }

    protected override void OnMouseMove(MouseEventArgs e) => Apply(() => _model.PointerMoved(new PixelPoint(e.X, e.Y)));

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Apply(() => _model.LeftReleased(new PixelPoint(e.X, e.Y)));
    }

    /// <summary>Passes the input to the model, then closes or repaints.</summary>
    private void Apply(Action input)
    {
        input();
        switch (_model.Outcome)
        {
            case SelectionOutcome.Accepted when _model.Result is { } local:
                Result = local.ToRectangle() with { X = local.X + _virtualScreen.X, Y = local.Y + _virtualScreen.Y };
                DialogResult = DialogResult.OK;
                break;
            case SelectionOutcome.Cancelled:
                DialogResult = DialogResult.Cancel;
                break;
            default:
                Invalidate();
                break;
        }
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

        if (_model.Highlight is { } highlight)
        {
            var rect = highlight.ToRectangle();
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

        if (_model.ShowCrosshair && _model.Cursor is { } cursor)
        {
            g.DrawLine(_crosshairPen, cursor.X, 0, cursor.X, Height);
            g.DrawLine(_crosshairPen, 0, cursor.Y, Width, cursor.Y);
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
        var text = _model.WindowMode
            ? "Mode fenêtre : cliquez sur une fenêtre   ·   Espace : mode zone   ·   Échap : annuler"
            : "Glissez pour sélectionner une zone   ·   Clic : fenêtre sous le curseur   ·   Espace : mode fenêtre   ·   Échap : annuler";

        // Centered at the top of the monitor under the cursor.
        var cursor = _model.Cursor ?? new PixelPoint(0, 0);
        var screen = Screen.FromPoint(new Point(cursor.X + _virtualScreen.X, cursor.Y + _virtualScreen.Y)).Bounds;
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
