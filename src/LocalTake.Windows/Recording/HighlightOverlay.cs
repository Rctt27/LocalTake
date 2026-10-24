using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using LocalTake.Adapters;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;

namespace LocalTake;

/// <summary>
/// The highlight trail, drawn over the recorded area like a highlighter stroke. Click-through and never activated, as
/// <see cref="RecordingFrame"/>, but on purpose not excluded from capture: gdigrab copies layered windows, so the video
/// shows the trail. The window's opacity makes the stroke translucent and fades it out without repainting.
/// </summary>
internal sealed class HighlightOverlay : Form, IHighlightOverlay
{
    internal static readonly Color Ink = Color.FromArgb(255, 212, 0);

    /// <summary>The stroke width at 100 %, scaled by the monitor's DPI.</summary>
    internal const float Thickness = 14.4f;

    private readonly Rectangle _bounds;
    private Point[] _points = [];

    public HighlightOverlay(Rectangle area)
    {
        _bounds = area;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _bounds;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        // Pixels of this color are fully transparent: only the stroke is visible.
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    private float StrokeWidth => Thickness * DeviceDpi / 96f;

    public void Draw(IReadOnlyList<PixelPoint> points)
    {
        var previous = _points;
        _points = [.. points.Select(p => new Point(p.X - _bounds.X, p.Y - _bounds.Y))];
        if (!Visible)
        {
            Show();
            Bounds = _bounds;
        }
        // While the trail grows, only its new end needs repainting; a new trail repaints everything.
        var grown = previous.Length > 0 && _points.Length > previous.Length && _points[0] == previous[0];
        Invalidate(grown ? Around(_points[(previous.Length - 1)..]) : ClientRectangle);
    }

    public void SetOpacity(double opacity) => Opacity = opacity;

    void IHighlightOverlay.Hide()
    {
        _points = [];
        Hide();
    }

    protected override void WndProc(ref Message m)
    {
        // Same reason as SelectionOverlay: the overlay is positioned in physical pixels and must not be rescaled.
        if (m.Msg == Native.WM_DPICHANGED)
            return;
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_points.Length == 0)
            return;
        // No antialiasing: edge pixels blended with the magenta key would leave a pink fringe.
        e.Graphics.SmoothingMode = SmoothingMode.None;
        var width = StrokeWidth;
        if (_points.Length == 1)
        {
            using var brush = new SolidBrush(Ink);
            e.Graphics.FillEllipse(brush, _points[0].X - width / 2, _points[0].Y - width / 2, width, width);
            return;
        }
        using var pen = new Pen(Ink, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.DrawLines(pen, _points);
    }

    private Rectangle Around(Point[] points)
    {
        int left = points.Min(p => p.X), top = points.Min(p => p.Y), right = points.Max(p => p.X), bottom = points.Max(p => p.Y);
        var margin = (int)System.Math.Ceiling(StrokeWidth);
        return Rectangle.FromLTRB(left - margin, top - margin, right + margin, bottom + margin);
    }
}

internal sealed class HighlightOverlayFactory : IHighlightOverlayFactory
{
    public IHighlightOverlay Create(PixelRect area) => new HighlightOverlay(area.ToRectangle());
}
