using LocalTake.Core.Files;
using LocalTake.Core.Settings;
using NSubstitute;

namespace LocalTake.Core.Tests.Files;

public sealed class FileCounterTests
{
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows("C:\\Out")) { FileNamePattern = "Shot_%NUM%", Counter = 5 };
    private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
    private readonly FileCounter _counter;

    public FileCounterTests() => _counter = new FileCounter(_settings, _store);

    [Fact]
    public void Consume_advances_the_counter_and_saves_it()
    {
        _counter.Consume();
        _settings.Counter.ShouldBe(6);
        _store.Received(1).TrySave(_settings);
    }

    [Fact]
    public void Consume_does_nothing_when_the_pattern_has_no_counter()
    {
        _settings.FileNamePattern = "Shot_%YYYY%";
        _counter.Consume();
        _settings.Counter.ShouldBe(5);
        _store.DidNotReceiveWithAnyArgs().TrySave(default!);
    }

    [Fact]
    public void Reserve_returns_the_number_taken()
    {
        _counter.Reserve().ShouldBe(5);
        _settings.Counter.ShouldBe(6);
    }

    [Fact]
    public void Reserve_returns_null_when_the_pattern_has_no_counter()
    {
        _settings.FileNamePattern = "x";
        _counter.Reserve().ShouldBeNull();
    }

    [Fact]
    public void Release_gives_the_number_back()
    {
        var reserved = _counter.Reserve();
        _counter.Release(reserved);
        _settings.Counter.ShouldBe(5);
    }

    [Fact]
    public void Release_keeps_the_counter_when_a_later_file_took_a_number_meanwhile()
    {
        var reserved = _counter.Reserve();
        _counter.Consume();
        _counter.Release(reserved);
        _settings.Counter.ShouldBe(7);
    }

    [Fact]
    public void Release_of_nothing_does_nothing()
    {
        _counter.Release(null);
        _settings.Counter.ShouldBe(5);
    }
}
