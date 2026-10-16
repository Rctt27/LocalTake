using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;

namespace LocalTake.App.Settings;

/// <summary>What the settings window needs from the rest of the app.</summary>
internal sealed record SettingsContext(
    AppSettings Settings, SettingsEditor Editor, SettingsDefaults Defaults, FfmpegLocator Locator, IFfmpegPlatform Platform,
    IAudioDeviceLister AudioDevices, IStartupRegistration Startup, HotkeyNaming Naming);

/// <summary>A choice in a list: the value stored, and its label.</summary>
internal sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsContext _context;
    private int _audioLoadGeneration;

    public SettingsViewModel(SettingsContext context)
    {
        _context = context;
        var form = SettingsForm.From(context.Settings);
        _outputFolder = form.OutputFolder;
        _fileNamePattern = form.FileNamePattern;
        _selectedFormat = Formats.FirstOrDefault(f => f.Value == form.Format) ?? Formats[0];
        _jpegQuality = form.JpegQuality;
        _copyPathAfterSave = form.CopyPathAfterSave;
        _selectedAfterCapture = AfterCaptureChoices.First(c => c.Value == form.AfterCapture);
        _hotkeyRegion = form.HotkeyRegion;
        _hotkeyWindow = form.HotkeyWindow;
        _hotkeyFullScreen = form.HotkeyFullScreen;
        _hotkeyLastRegion = form.HotkeyLastRegion;
        _hotkeyVideoRegion = form.HotkeyVideoRegion;
        _hotkeyVideoFullScreen = form.HotkeyVideoFullScreen;
        _hotkeyVideoStart = form.HotkeyVideoStart;
        _hotkeyVideoStop = form.HotkeyVideoStop;
        _ffmpegPath = form.FfmpegPath;
        _recordMicrophone = form.RecordMicrophone;
        _recordSystemAudio = form.RecordSystemAudio;
        _videoFrameRate = form.VideoFrameRate;
        _startWithSystem = context.Startup.IsEnabled;
        _savedMicrophone = form.Microphone;
        Microphones.Add(new Choice<string>("", "Premier micro détecté"));
        if (form.Microphone.Length > 0)
            Microphones.Add(new Choice<string>(form.Microphone, form.Microphone));
        _selectedMicrophone = Microphones[^1];
        UpdateFfmpegStatus();
    }

    public static IReadOnlyList<Choice<string>> Formats { get; } = [new("png", "PNG"), new("jpg", "JPEG")];

    public static IReadOnlyList<Choice<AfterCaptureAction>> AfterCaptureChoices { get; } =
    [
        new(AfterCaptureAction.OpenEditor, "Ouvrir l'éditeur d'annotations"),
        new(AfterCaptureAction.SaveToFile, "Enregistrer directement dans le dossier"),
        new(AfterCaptureAction.CopyToClipboard, "Copier dans le presse-papier"),
        new(AfterCaptureAction.SaveAndCopy, "Enregistrer et copier dans le presse-papier")
    ];

    public static IReadOnlyList<int> FrameRates => SettingsForm.FrameRates;

    public ObservableCollection<Choice<string>> Microphones { get; } = [];

    public HotkeyNaming Naming => _context.Naming;

    public string StartupLabel => _context.Naming == HotkeyNaming.MacOS
        ? "Lancer LocalTake à l'ouverture de session"
        : "Lancer LocalTake au démarrage de Windows";

    public string DefaultHotkeysLabel => _context.Naming == HotkeyNaming.MacOS ? "Raccourcis par défaut" : "Raccourcis de TinyTake";

    private readonly string _savedMicrophone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatternPreview))]
    private string _outputFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatternPreview))]
    private string _fileNamePattern;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatternPreview), nameof(IsJpeg))]
    private Choice<string> _selectedFormat;

    [ObservableProperty]
    private int _jpegQuality;

    [ObservableProperty]
    private bool _copyPathAfterSave;

    [ObservableProperty]
    private Choice<AfterCaptureAction> _selectedAfterCapture;

    [ObservableProperty]
    private string _hotkeyRegion;

    [ObservableProperty]
    private string _hotkeyWindow;

    [ObservableProperty]
    private string _hotkeyFullScreen;

    [ObservableProperty]
    private string _hotkeyLastRegion;

    [ObservableProperty]
    private string _hotkeyVideoRegion;

    [ObservableProperty]
    private string _hotkeyVideoFullScreen;

    [ObservableProperty]
    private string _hotkeyVideoStart;

    [ObservableProperty]
    private string _hotkeyVideoStop;

    [ObservableProperty]
    private string _ffmpegPath;

    [ObservableProperty]
    private string _ffmpegStatus = "";

    [ObservableProperty]
    private bool _ffmpegMissing;

    [ObservableProperty]
    private Choice<string> _selectedMicrophone;

    [ObservableProperty]
    private bool _recordMicrophone;

    [ObservableProperty]
    private bool _recordSystemAudio;

    [ObservableProperty]
    private bool _systemAudioAvailable = true;

    [ObservableProperty]
    private string _systemAudioTip = "";

    [ObservableProperty]
    private int _videoFrameRate;

    [ObservableProperty]
    private bool _startWithSystem;

    /// <summary>The last error; the window shows it next to the buttons.</summary>
    [ObservableProperty]
    private string? _error;

    public bool IsJpeg => SelectedFormat.Value == "jpg";

    public string PatternPreview
    {
        get
        {
            var example = Core.Files.FileNamePattern.Expand(FileNamePattern.Trim(), _context.Settings.Counter, DateTime.Now);
            return $"Exemple : {example}.{SelectedFormat.Value}   ·   Variables : %YYYY% %MM% %DD% %hh% %mm% %ss% %NUM%";
        }
    }

    /// <summary>Raised once the settings are saved, or cancelled: the window closes.</summary>
    public event EventHandler? Finished;

    partial void OnFfmpegPathChanged(string value) => UpdateFfmpegStatus();

    /// <returns>The ffmpeg found, or null.</returns>
    public string? UpdateFfmpegStatus()
    {
        var path = _context.Locator.Find(FfmpegPath.Trim());
        FfmpegMissing = path is null;
        FfmpegStatus = path is not null
            ? (string.IsNullOrWhiteSpace(FfmpegPath) ? "Détecté : " + path : "ffmpeg trouvé.")
            : "ffmpeg introuvable : les vidéos ne pourront pas être enregistrées. " + _context.Platform.InstallHint;
        return path;
    }

    /// <summary>Asks ffmpeg for the audio inputs; a newer request (another ffmpeg chosen) wins.</summary>
    public async Task LoadAudioDevicesAsync()
    {
        var generation = ++_audioLoadGeneration;
        // Keep the choice being edited, not only the saved one.
        var selected = SelectedMicrophone?.Value ?? _savedMicrophone;
        IReadOnlyList<string> devices = UpdateFfmpegStatus() is { } ffmpeg ? await _context.AudioDevices.ListAsync(ffmpeg) : [];
        if (generation != _audioLoadGeneration)
            return;

        Microphones.Clear();
        Microphones.Add(new Choice<string>("", "Premier micro détecté"));
        // A chosen microphone stays selectable even if it is unplugged right now.
        var names = AudioDevices.Microphones(devices, _context.Platform.SystemAudioDevice).ToList();
        if (selected.Length > 0 && !names.Contains(selected))
            names.Add(selected);
        foreach (var name in names)
            Microphones.Add(new Choice<string>(name, name));
        SelectedMicrophone = Microphones.FirstOrDefault(m => m.Value == selected) ?? Microphones[0];

        var system = _context.Platform.SystemAudioDevice;
        SystemAudioAvailable = devices.Contains(system) || RecordSystemAudio;
        SystemAudioTip = devices.Contains(system)
            ? $"Utilise {_context.Platform.SystemAudioRequirement}."
            : $"Nécessite {_context.Platform.SystemAudioRequirement} : introuvable sur cet ordinateur.";
    }

    [RelayCommand]
    private void DefaultHotkeys()
    {
        var d = _context.Defaults;
        HotkeyRegion = d.HotkeyRegion;
        HotkeyWindow = d.HotkeyWindow;
        HotkeyFullScreen = d.HotkeyFullScreen;
        HotkeyLastRegion = d.HotkeyLastRegion;
        HotkeyVideoRegion = d.HotkeyVideoRegion;
        HotkeyVideoFullScreen = d.HotkeyVideoFullScreen;
        HotkeyVideoStart = d.HotkeyVideoStart;
        HotkeyVideoStop = d.HotkeyVideoStop;
    }

    [RelayCommand]
    private void Save()
    {
        var form = new SettingsForm(
            OutputFolder, FileNamePattern, SelectedFormat.Value, JpegQuality, CopyPathAfterSave, SelectedAfterCapture.Value,
            HotkeyRegion, HotkeyWindow, HotkeyFullScreen, HotkeyLastRegion, HotkeyVideoRegion, HotkeyVideoFullScreen,
            HotkeyVideoStart, HotkeyVideoStop, FfmpegPath, SelectedMicrophone?.Value ?? _savedMicrophone, RecordMicrophone,
            RecordSystemAudio, VideoFrameRate);
        Error = _context.Editor.TryApply(form);
        if (Error is not null)
            return;
        try
        {
            if (_context.Startup.IsEnabled != StartWithSystem)
                _context.Startup.SetEnabled(StartWithSystem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Error = "Le lancement au démarrage n'a pas pu être modifié : " + ex.Message;
            return;
        }
        Finished?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Finished?.Invoke(this, EventArgs.Empty);
}
