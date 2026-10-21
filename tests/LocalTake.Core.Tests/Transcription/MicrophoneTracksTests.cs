using System.IO.Abstractions.TestingHelpers;
using LocalTake.Core.Ports;
using LocalTake.Core.Settings;
using LocalTake.Core.Transcription;
using NSubstitute;

namespace LocalTake.Core.Tests.Transcription;

public sealed class MicrophoneTracksTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "LocalTakeTracks");
    private static readonly string Models = Path.Combine(Root, "models");
    private static readonly string Work = Path.Combine(Root, "work");

    private readonly MockFileSystem _fs = new();
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(Root)) { TranscribeMicrophone = true };
    private readonly MicrophoneTracks _tracks;

    public MicrophoneTracksTests()
    {
        _fs.AddFile(Path.Combine(Models, "ggml-small-q5_1.bin"), new MockFileData(""));
        var paths = Substitute.For<IAppPaths>();
        paths.WorkFolder.Returns(Work);
        _tracks = new MicrophoneTracks(_settings, new TranscriptionModelLocator(_fs, [Models]), paths, _fs);
    }

    [Fact]
    public void A_track_is_a_new_wav_in_the_work_folder()
    {
        var first = _tracks.Allocate();
        var second = _tracks.Allocate();

        Path.GetDirectoryName(first).ShouldBe(Work);
        Path.GetExtension(first).ShouldBe(".wav");
        second.ShouldNotBe(first);
        _fs.Directory.Exists(Work).ShouldBeTrue();
    }

    [Fact]
    public void A_track_is_recorded_even_without_the_automatic_transcription()
    {
        // The player can still transcribe it afterwards, with its "Générer les sous-titres" button.
        _settings.TranscribeMicrophone = false;
        _tracks.Allocate().ShouldNotBeNull();
    }

    [Fact]
    public void No_track_without_a_model()
    {
        _fs.File.Delete(Path.Combine(Models, "ggml-small-q5_1.bin"));

        _tracks.CanTranscribe.ShouldBeFalse();
        _tracks.Allocate().ShouldBeNull();
    }

    [Fact]
    public void Leftover_tracks_are_deleted_and_nothing_else()
    {
        _fs.AddFile(Path.Combine(Work, "old.wav"), new MockFileData(""));
        _fs.AddFile(Path.Combine(Work, "keep.txt"), new MockFileData(""));

        _tracks.DeleteLeftovers();

        _fs.Directory.GetFiles(Work).ShouldBe([Path.Combine(Work, "keep.txt")]);
    }

    [Fact]
    public void Without_a_work_folder_there_is_nothing_to_delete() => Should.NotThrow(_tracks.DeleteLeftovers);
}
