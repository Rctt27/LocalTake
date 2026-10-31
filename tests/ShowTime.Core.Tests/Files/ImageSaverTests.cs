using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShowTime.Core.Files;
using ShowTime.Core.Ports;
using ShowTime.Core.Settings;

namespace ShowTime.Core.Tests.Files;

public sealed class ImageSaverTests
{
    private static readonly string Out = Path.Combine(Path.GetTempPath(), "ShowTimeSaver", "out");
    private static readonly string Elsewhere = Path.Combine(Path.GetTempPath(), "ShowTimeSaver", "elsewhere");

    private readonly MockFileSystem _fs = new();
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(Out)) { FileNamePattern = "Shot_%NUM%", Counter = 3 };
    private readonly MemorySettingsStore _store = new();
    private readonly IImageCodec _codec = Substitute.For<IImageCodec>();
    private readonly IClipboard _clipboard = Substitute.For<IClipboard>();
    private readonly ISaveFileDialog _dialog = Substitute.For<ISaveFileDialog>();
    private readonly FakeImage _image = new();
    private readonly ImageSaver _saver;

    public ImageSaverTests()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 7, 9, 5, 2, TimeSpan.Zero));
        _saver = new ImageSaver(_settings, _store, new OutputPathAllocator(_fs, clock), new FileCounter(_settings, _store), _codec, _clipboard, _fs);
    }

    [Fact]
    public void SaveToOutputFolder_writes_the_next_name_and_advances_the_counter()
    {
        var path = _saver.SaveToOutputFolder(_image);

        path.ShouldBe(Path.Combine(Out, "Shot_000003.png"));
        _codec.Received(1).Save(_image, path, 90);
        _settings.Counter.ShouldBe(4);
        _clipboard.DidNotReceiveWithAnyArgs().SetText(default!);
    }

    [Fact]
    public void SaveToOutputFolder_uses_the_JPEG_extension_and_quality()
    {
        _settings.Format = "jpg";
        _settings.JpegQuality = 70;
        var path = _saver.SaveToOutputFolder(_image);
        path.ShouldEndWith(".jpg");
        _codec.Received(1).Save(_image, path, 70);
    }

    [Fact]
    public void SaveToOutputFolder_copies_the_path_when_asked()
    {
        _settings.CopyPathAfterSave = true;
        var path = _saver.SaveToOutputFolder(_image);
        _clipboard.Received(1).SetText(path);
    }

    [Fact]
    public void A_failed_save_keeps_the_counter()
    {
        _codec.When(c => c.Save(Arg.Any<IImage>(), Arg.Any<string>(), Arg.Any<int>())).Throw(new IOException("disk full"));
        Should.Throw<IOException>(() => _saver.SaveToOutputFolder(_image));
        _settings.Counter.ShouldBe(3);
    }

    [Fact]
    public async Task SaveAs_suggests_the_next_name_in_the_output_folder()
    {
        await _saver.SaveAsAsync(_image, _dialog);
        await _dialog.Received(1).AskAsync("Shot_000003", false, Out);
    }

    [Fact]
    public async Task SaveAs_opens_where_the_last_Save_As_went_when_that_folder_still_exists()
    {
        _fs.AddDirectory(Elsewhere);
        _settings.LastSaveAsFolder = Elsewhere;
        _settings.Format = "jpg";
        await _saver.SaveAsAsync(_image, _dialog);
        await _dialog.Received(1).AskAsync("Shot_000003", true, Elsewhere);
    }

    [Fact]
    public async Task SaveAs_cancelled_saves_nothing()
    {
        _dialog.AskAsync(default!, default, default!).ReturnsForAnyArgs((string?)null);
        (await _saver.SaveAsAsync(_image, _dialog)).ShouldBeNull();
        _codec.DidNotReceiveWithAnyArgs().Save(default!, default!, default);
        _settings.Counter.ShouldBe(3);
    }

    [Fact]
    public async Task SaveAs_with_the_suggested_name_uses_up_the_number_and_remembers_the_folder()
    {
        var chosen = Path.Combine(Elsewhere, "shot_000003.jpg");
        _dialog.AskAsync(default!, default, default!).ReturnsForAnyArgs(chosen);

        (await _saver.SaveAsAsync(_image, _dialog)).ShouldBe(chosen);

        _codec.Received(1).Save(_image, chosen, 90);
        _settings.Counter.ShouldBe(4);
        _settings.LastSaveAsFolder.ShouldBe(Elsewhere);
        _store.Saves.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SaveAs_with_another_name_keeps_the_number()
    {
        _dialog.AskAsync(default!, default, default!).ReturnsForAnyArgs(Path.Combine(Elsewhere, "mine.png"));
        await _saver.SaveAsAsync(_image, _dialog);
        _settings.Counter.ShouldBe(3);
    }

    [Fact]
    public async Task SaveAs_copies_the_path_when_asked()
    {
        _settings.CopyPathAfterSave = true;
        var chosen = Path.Combine(Elsewhere, "mine.png");
        _dialog.AskAsync(default!, default, default!).ReturnsForAnyArgs(chosen);
        await _saver.SaveAsAsync(_image, _dialog);
        _clipboard.Received(1).SetText(chosen);
    }
}
