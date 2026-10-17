using System.IO.Abstractions.TestingHelpers;
using LocalTake.Core.Settings;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LocalTake.Core.Tests.Settings;

public sealed class SettingsEditorTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "LocalTakeEditor");
    private static readonly string Folder = Path.Combine(Root, "Captures");

    private readonly MockFileSystem _fs = new();
    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(Folder));
    private readonly ISettingsStore _store = Substitute.For<ISettingsStore>();
    private readonly SettingsEditor _editor;

    public SettingsEditorTests() => _editor = new SettingsEditor(_settings, _store, _fs, @"C:\Users\Moi\Captures");

    private SettingsForm Form() => SettingsForm.From(_settings);

    [Fact]
    public void From_and_ApplyTo_round_trip_every_field()
    {
        _settings.Format = "jpg";
        _settings.HotkeyLastRegion = "Ctrl+L";
        _settings.VideoFrameRate = 60;
        var copy = new AppSettings(SettingsDefaults.ForMacOS("/x"));

        SettingsForm.From(_settings).ApplyTo(copy);

        copy.Format.ShouldBe("jpg");
        copy.HotkeyLastRegion.ShouldBe("Ctrl+L");
        copy.HotkeyRegion.ShouldBe("PrintScreen");
        copy.VideoFrameRate.ShouldBe(60);
        copy.OutputFolder.ShouldBe(Folder);
    }

    [Fact]
    public void A_valid_form_is_trimmed_applied_and_saved()
    {
        var form = Form() with { FileNamePattern = "  Shot_%NUM%  ", HotkeyRegion = " Ctrl+1 ", OutputFolder = "  " + Folder + "  " };

        _editor.TryApply(form).ShouldBeNull();

        _settings.FileNamePattern.ShouldBe("Shot_%NUM%");
        _settings.HotkeyRegion.ShouldBe("Ctrl+1");
        _settings.OutputFolder.ShouldBe(Folder);
        _fs.Directory.Exists(Folder).ShouldBeTrue();
        _store.Received(1).Save(_settings);
    }

    [Fact]
    public void A_shortcut_given_twice_is_refused_whatever_its_case()
    {
        var form = Form() with { HotkeyWindow = "printscreen" };
        _editor.TryApply(form).ShouldBe("Le raccourci PrintScreen est attribué deux fois.");
        _store.DidNotReceiveWithAnyArgs().Save(default!);
    }

    [Fact]
    public void Empty_shortcuts_are_not_duplicates() =>
        _editor.TryApply(Form() with { HotkeyLastRegion = "", HotkeyVideoFullScreen = " " }).ShouldBeNull();

    [Theory]
    [InlineData("", "x")]
    [InlineData("C:\\x", " ")]
    public void Folder_and_pattern_are_required(string folder, string pattern) =>
        _editor.TryApply(Form() with { OutputFolder = folder, FileNamePattern = pattern })
            .ShouldBe("Le dossier et le nom des fichiers sont obligatoires.");

    [Fact]
    public void A_relative_folder_is_refused_with_an_example() =>
        _editor.TryApply(Form() with { OutputFolder = "Captures" })
            .ShouldBe(@"Indiquez le chemin complet du dossier, par exemple C:\Users\Moi\Captures.");

    [Fact]
    public void A_missing_ffmpeg_file_is_refused() =>
        _editor.TryApply(Form() with { FfmpegPath = Path.Combine(Root, "ffmpeg.exe") })
            .ShouldBe("Le fichier ffmpeg indiqué n'existe pas. Laissez le champ vide pour une détection automatique.");

    [Fact]
    public void An_existing_ffmpeg_file_is_kept()
    {
        var ffmpeg = Path.Combine(Root, "bin", "ffmpeg.exe");
        _fs.AddFile(ffmpeg, new MockFileData(""));
        _editor.TryApply(Form() with { FfmpegPath = " " + ffmpeg }).ShouldBeNull();
        _settings.FfmpegPath.ShouldBe(ffmpeg);
    }

    [Fact]
    public void A_folder_that_cannot_be_created_is_reported()
    {
        var blocked = Path.Combine(Root, "file.txt");
        _fs.AddFile(blocked, new MockFileData(""));
        _editor.TryApply(Form() with { OutputFolder = blocked })!.ShouldStartWith("Ce dossier est inutilisable : ");
    }

    [Fact]
    public void A_settings_file_that_cannot_be_written_is_reported()
    {
        _store.When(s => s.Save(Arg.Any<AppSettings>())).Throw(new IOException("locked"));
        _editor.TryApply(Form()).ShouldBe("Les réglages n'ont pas pu être enregistrés : locked");
    }

    [Theory]
    [InlineData(29, 30)]
    [InlineData(5, 15)]
    [InlineData(100, 60)]
    public void Frame_rate_is_the_nearest_offered(int saved, int shown) =>
        SettingsForm.From(new AppSettings(SettingsDefaults.ForWindows("x")) { VideoFrameRate = saved }).VideoFrameRate.ShouldBe(shown);
}
