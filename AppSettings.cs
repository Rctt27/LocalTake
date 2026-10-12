using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalTake;

public enum AfterCaptureAction
{
    OpenEditor,
    SaveToFile,
    CopyToClipboard,
    SaveAndCopy
}

public sealed record RegionRect(int X, int Y, int Width, int Height);

public sealed class AppSettings
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalTake");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string OutputFolder { get; set; } = DefaultOutputFolder();

    /// <summary>Placeholders: %YYYY% %MM% %DD% %hh% %mm% %ss% %NUM%.</summary>
    public string FileNamePattern { get; set; } = "Capture_%YYYY%-%MM%-%DD%_%hh%-%mm%-%ss%";

    /// <summary>"png" or "jpg".</summary>
    public string Format { get; set; } = "png";

    public int JpegQuality { get; set; } = 90;

    public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.OpenEditor;

    public bool CopyPathAfterSave { get; set; }

    public string HotkeyRegion { get; set; } = "PrintScreen";
    public string HotkeyWindow { get; set; } = "Alt+PrintScreen";
    public string HotkeyFullScreen { get; set; } = "Ctrl+PrintScreen";
    public string HotkeyLastRegion { get; set; } = "";
    public string HotkeyVideoRegion { get; set; } = "Ctrl+Shift+PrintScreen";
    public string HotkeyVideoFullScreen { get; set; } = "";

    /// <summary>Empty: ffmpeg.exe next to LocalTake, then on the PATH.</summary>
    public string FfmpegPath { get; set; } = "";

    /// <summary>DirectShow audio device name; empty for no microphone.</summary>
    public string Microphone { get; set; } = "";

    public bool RecordSystemAudio { get; set; }

    public int VideoFrameRate { get; set; } = 30;

    public RegionRect? LastRegion { get; set; }

    /// <summary>Next value of %NUM%.</summary>
    public int Counter { get; set; } = 1;

    public string? LastSaveAsFolder { get; set; }

    public string EditorColor { get; set; } = "#E53935";
    public double EditorThickness { get; set; } = 4;
    public double EditorFontSize { get; set; } = 24;

    /// <summary>
    /// Pictures\LocalTake, unless Pictures is synced by OneDrive: captures must stay on this computer.
    /// </summary>
    private static string DefaultOutputFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var syncRoots = new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .Where(root => !string.IsNullOrEmpty(root));
        var synced = string.IsNullOrEmpty(pictures) ||
                     syncRoots.Any(root => pictures.StartsWith(root!, StringComparison.OrdinalIgnoreCase));
        return synced
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Captures LocalTake")
            : Path.Combine(pictures, "LocalTake");
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt file must not prevent the app from starting; defaults take over and overwrite it on next save.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>
    /// For incidental state (last region, counter, editor preferences): an antivirus or a backup tool briefly
    /// locking the file must not interrupt a capture.
    /// </summary>
    public bool TrySave()
    {
        try
        {
            Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
