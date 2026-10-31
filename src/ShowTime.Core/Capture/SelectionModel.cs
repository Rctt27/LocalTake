using ShowTime.Core.Geometry;

namespace ShowTime.Core.Capture;

public enum SelectionOutcome
{
    Pending,
    Accepted,
    Cancelled
}

public enum SelectionKey
{
    Escape,
    Space,
    Enter
}

/// <summary>
/// What the selection overlay does with the mouse and keyboard: drag to select a region, click to take the window
/// under the cursor, Space to switch to window mode, Escape or right-click to cancel. Coordinates are the overlay's,
/// in physical pixels; the view only draws <see cref="Highlight"/> and reports the input.
/// </summary>
public sealed class SelectionModel(IReadOnlyList<PixelRect> windows, bool windowMode)
{
    /// <summary>A smaller drag is taken as a click on the window under the cursor.</summary>
    private const int MinimumSize = 3;

    private PixelPoint? _dragStart;
    private PixelRect _selection;
    private PixelRect? _hoverWindow;

    public bool WindowMode { get; private set; } = windowMode;
    public bool IsDragging => _dragStart is not null;
    public PixelPoint? Cursor { get; private set; }
    public SelectionOutcome Outcome { get; private set; }

    /// <summary>The accepted area, in overlay coordinates.</summary>
    public PixelRect? Result { get; private set; }

    /// <summary>What the overlay shows undimmed: the area being dragged, otherwise the window under the cursor.</summary>
    public PixelRect? Highlight => IsDragging && !WindowMode && _selection.Width > 0 ? _selection : _hoverWindow;

    public bool ShowCrosshair => !WindowMode && !IsDragging && Cursor is not null;

    /// <summary>Window rectangles converted to overlay coordinates; those off the screens are dropped.</summary>
    public static IReadOnlyList<PixelRect> ToLocal(IEnumerable<PixelRect> windows, PixelRect virtualScreen) =>
        windows.Select(w => w.Intersect(virtualScreen).Offset(-virtualScreen.X, -virtualScreen.Y))
            .Where(w => w.Width > 1 && w.Height > 1)
            .ToList();

    public void PointerMoved(PixelPoint p)
    {
        if (Outcome != SelectionOutcome.Pending)
            return;
        Cursor = p;
        if (_dragStart is { } start && !WindowMode)
            _selection = PixelRect.Between(start, p);
        else
            _hoverWindow = WindowAt(p);
    }

    public void LeftPressed(PixelPoint p)
    {
        if (Outcome != SelectionOutcome.Pending)
            return;
        _dragStart = p;
        _selection = default;
    }

    public void LeftReleased(PixelPoint p)
    {
        if (Outcome != SelectionOutcome.Pending || _dragStart is null)
            return;
        _dragStart = null;

        if (!WindowMode && _selection.Width >= MinimumSize && _selection.Height >= MinimumSize)
            Accept(_selection);
        else if (WindowAt(p) is { } window)
            Accept(window);
    }

    /// <summary>Cancels the drag under way, otherwise the whole selection.</summary>
    public void RightPressed()
    {
        if (Outcome != SelectionOutcome.Pending)
            return;
        if (_dragStart is null)
            Outcome = SelectionOutcome.Cancelled;
        _dragStart = null;
    }

    public void KeyPressed(SelectionKey key)
    {
        if (Outcome != SelectionOutcome.Pending)
            return;
        switch (key)
        {
            case SelectionKey.Escape:
                Outcome = SelectionOutcome.Cancelled;
                break;
            case SelectionKey.Space when _dragStart is null:
                WindowMode = !WindowMode;
                break;
            case SelectionKey.Enter when _hoverWindow is { } window:
                Accept(window);
                break;
        }
    }

    private PixelRect? WindowAt(PixelPoint p)
    {
        foreach (var w in windows)
            if (w.Contains(p))
                return w;
        return null;
    }

    private void Accept(PixelRect area)
    {
        Result = area;
        Outcome = SelectionOutcome.Accepted;
    }
}
