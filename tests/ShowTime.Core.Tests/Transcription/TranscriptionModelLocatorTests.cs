using System.IO.Abstractions.TestingHelpers;
using ShowTime.Core.Transcription;

namespace ShowTime.Core.Tests.Transcription;

public sealed class TranscriptionModelLocatorTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ShowTimeModels");
    private static readonly string AppModels = Path.Combine(Root, "app", "models");
    private static readonly string DataModels = Path.Combine(Root, "data", "models");

    private readonly MockFileSystem _fs = new();

    private TranscriptionModelLocator Locator => new(_fs, [AppModels, DataModels]);

    private string Add(string directory, string name)
    {
        var file = Path.Combine(directory, name);
        _fs.AddFile(file, new MockFileData(""));
        return file;
    }

    [Fact]
    public void A_configured_model_wins_when_it_exists()
    {
        var configured = Add(Path.Combine(Root, "custom"), "mine.bin");
        Add(AppModels, "ggml-small-q5_1.bin");

        Locator.Find(configured).ShouldBe(configured);
    }

    [Fact]
    public void A_configured_model_that_does_not_exist_finds_nothing_rather_than_another_one()
    {
        Add(AppModels, "ggml-small-q5_1.bin");

        Locator.Find(Path.Combine(Root, "missing.bin")).ShouldBeNull();
    }

    [Fact]
    public void Otherwise_the_models_folder_next_to_the_app_comes_before_the_data_folder()
    {
        Add(DataModels, "ggml-base.bin");
        var shipped = Add(AppModels, "ggml-small-q5_1.bin");

        Locator.Find("").ShouldBe(shipped);
    }

    [Fact]
    public void In_a_folder_the_first_model_by_name_is_taken_and_other_files_are_ignored()
    {
        Add(DataModels, "readme.txt");
        Add(DataModels, "ggml-small.bin");
        var first = Add(DataModels, "ggml-base.bin");

        Locator.Find(" ").ShouldBe(first);
    }

    [Fact]
    public void Without_any_model_nothing_is_found() => Locator.Find("").ShouldBeNull();
}
