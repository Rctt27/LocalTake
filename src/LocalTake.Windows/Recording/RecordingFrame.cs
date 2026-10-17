using System.Drawing;
using System.Windows.Forms;
using LocalTake.Core.Ports;

namespace LocalTake;

/// <summary>
/// Red outline drawn just outside the recorded area. Click-through, never activated, and excluded from capture,
/// so it cannot appear in the video even where it overlaps the area.
/// </summary>
internal sealed class RecordingFrame : Form
{
    private const int Thickness = 3;
    private readonly Rectangle _bounds;
    private readonly INativeWindows _windows;

    public RecordingFrame(Rectangle area, INativeWindows windows)
    {
        _windows = windows;
        _bounds = Rectangle.Inflate(area, Thickness, Thickness);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _bounds;
        ShowInTaskbar = false;
        TopMost = true;
        // Pixels of this color are fully transparent: only the outline is visible.
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

    protected override void OnHandleCreated(System.EventArgs e)
    {
        base.OnHandleCreated(e);
        _windows.ExcludeFromCapture(Handle);
    }

    protected override void OnShown(System.EventArgs e)
    {
        base.OnShown(e);
        Bounds = _bounds;
    }

    protected override void WndProc(ref Message m)
    {
        // Same reason as SelectionOverlay: the outline is positioned in physical pixels and must not be rescaled.
        if (m.Msg == Native.WM_DPICHANGED)
            return;
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new SolidBrush(Color.FromArgb(229, 57, 53));
        e.Graphics.FillRectangle(brush, 0, 0, Width, Thickness);
        e.Graphics.FillRectangle(brush, 0, Height - Thickness, Width, Thickness);
        e.Graphics.FillRectangle(brush, 0, 0, Thickness, Height);
        e.Graphics.FillRectangle(brush, Width - Thickness, 0, Thickness, Height);
    }
}
