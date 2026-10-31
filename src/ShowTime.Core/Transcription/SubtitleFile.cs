using System.IO.Abstractions;
using System.Text;

namespace ShowTime.Core.Transcription;

/// <summary>The .srt file next to a video, with the same name, where every player looks for its subtitles.</summary>
public sealed class SubtitleFile(IFileSystem fileSystem)
{
    /// <summary>With a byte order mark: without one, Windows players read accented letters in the wrong encoding.</summary>
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static string PathFor(string videoPath) => Path.ChangeExtension(videoPath, ".srt");

    /// <summary>The video's subtitles, or null when it has none.</summary>
    public SubtitleDocument? Load(string videoPath)
    {
        var path = PathFor(videoPath);
        return fileSystem.File.Exists(path) ? new SubtitleDocument(SrtFormat.Parse(fileSystem.File.ReadAllText(path))) : null;
    }

    /// <summary>
    /// Writes a temporary file, then puts it in place: a failure halfway leaves the previous subtitles. A cue still
    /// without text is left out: SubRip has no empty cue. Without any cue left, the file is deleted.
    /// </summary>
    public void Save(string videoPath, IEnumerable<SubtitleCue> cues)
    {
        var path = PathFor(videoPath);
        var written = cues.Where(c => c.Text.Length > 0).ToList();
        if (written.Count == 0)
        {
            fileSystem.File.Delete(path);
            return;
        }
        var temporary = path + ".tmp";
        fileSystem.File.WriteAllText(temporary, SrtFormat.Write(written), Utf8WithBom);
        fileSystem.File.Move(temporary, path, overwrite: true);
    }
}
