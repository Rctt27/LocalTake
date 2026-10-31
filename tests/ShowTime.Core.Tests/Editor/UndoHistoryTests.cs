using ShowTime.Core.Editor;

namespace ShowTime.Core.Tests.Editor;

public sealed class UndoHistoryTests
{
    private readonly UndoHistory _history = new();
    private readonly List<string> _log = [];

    private void Push(string name) => _history.Push(() => _log.Add("undo " + name), () => _log.Add("redo " + name));

    [Fact]
    public void Undo_and_redo_run_the_steps_in_reverse_then_forward_order()
    {
        Push("a");
        Push("b");
        _history.Undo().ShouldBeTrue();
        _history.Undo().ShouldBeTrue();
        _history.Redo().ShouldBeTrue();
        _log.ShouldBe(["undo b", "undo a", "redo a"]);
    }

    [Fact]
    public void A_new_step_clears_what_could_be_redone()
    {
        Push("a");
        _history.Undo();
        Push("b");
        _history.CanRedo.ShouldBeFalse();
        _history.Redo().ShouldBeFalse();
    }

    [Fact]
    public void Undo_with_nothing_to_undo_does_nothing()
    {
        _history.CanUndo.ShouldBeFalse();
        _history.Undo().ShouldBeFalse();
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void Changed_is_raised_by_each_operation()
    {
        var changes = 0;
        _history.Changed += (_, _) => changes++;
        Push("a");
        _history.Undo();
        _history.Redo();
        changes.ShouldBe(3);
    }
}
