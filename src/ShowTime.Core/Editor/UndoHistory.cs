namespace ShowTime.Core.Editor;

/// <summary>Undo and redo as pairs of actions, pushed by each editing operation.</summary>
public sealed class UndoHistory
{
    private readonly Stack<(Action Undo, Action Redo)> _undo = new();
    private readonly Stack<(Action Undo, Action Redo)> _redo = new();

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records an operation already applied; what could be redone is lost.</summary>
    public void Push(Action undo, Action redo)
    {
        _undo.Push((undo, redo));
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var step))
            return false;
        step.Undo();
        _redo.Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var step))
            return false;
        step.Redo();
        _undo.Push(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
