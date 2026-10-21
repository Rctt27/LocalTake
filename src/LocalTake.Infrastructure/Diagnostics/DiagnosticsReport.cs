using System.Text;
using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using LocalTake.Core.Transcription;
using LocalTake.Infrastructure.Transcription;
using Microsoft.Extensions.Logging;

namespace LocalTake.Infrastructure.Diagnostics;

/// <summary>
/// "LocalTake --diagnostics": checks what LocalTake needs from this computer, without opening any window, and prints
/// the report (also written to the log). Meant to be attached to a bug report, on macOS especially.
/// </summary>
/// <param name="loadModel">
/// Loads the transcription model, returning why it failed or null; Whisper's native library by default, which a
/// published LocalTake finds in its runtimes folder.
/// </param>
public sealed class DiagnosticsReport(
    IAppPaths paths,
    AppSettings settings,
    FfmpegLocator locator,
    IFfmpegPlatform platform,
    IAudioDeviceLister audio,
    IScreenGrabber grabber,
    IGlobalHotkeys hotkeys,
    TranscriptionModelLocator models,
    ILogger<DiagnosticsReport> log,
    Func<string, string?>? loadModel = null)
{
    /// <returns>0 when every check passed, otherwise 2.</returns>
    public async Task<int> RunAsync(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var report = new StringBuilder();
        void Line(string text)
        {
            report.AppendLine(text);
            output.WriteLine(text);
            output.Flush();
        }
        var failures = 0;

        void Check(string name, Func<string> probe)
        {
            try
            {
                Line($"[ok]   {name}: {probe()}");
            }
#pragma warning disable CA1031 // A diagnostic reports every failure instead of stopping at the first.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failures++;
                Line($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Line($"LocalTake {typeof(DiagnosticsReport).Assembly.GetName().Version} — {Environment.OSVersion} — {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        Line($"Settings: {paths.SettingsFile}");
        Line($"Log:      {paths.LogsFolder}");
        Line($"Output:   {settings.OutputFolder}");

        var ffmpeg = locator.Find(settings.FfmpegPath);
        Check("ffmpeg", () => ffmpeg ?? throw new FileNotFoundException(platform.InstallHint));
        if (ffmpeg is not null)
        {
            // No ConfigureAwait(false): the checks below must stay on the caller's thread, which owns the shortcut window.
#pragma warning disable CA2007
            var devices = await audio.ListAsync(ffmpeg);
#pragma warning restore CA2007
            Check("audio inputs", () => devices.Count > 0 ? string.Join(", ", devices) : throw new InvalidOperationException("none listed"));
            Check("computer sound", () => platform.CanRecordSystemAudio(devices)
                ? "available"
                : throw new InvalidOperationException("missing: " + platform.SystemAudioRequirement));
        }

        Check("screen capture", () =>
        {
            var image = grabber.Grab(new PixelRect(0, 0, 64, 64));
            // A capture without the permission comes back uniform (the wallpaper only, or black).
            var distinct = Enumerable.Range(0, image.Width * image.Height).Select(i => BitConverter.ToInt32(image.Pixels, i * 4)).Distinct().Count();
            return $"{image.Width}×{image.Height}, {distinct} colours";
        });
        Check("window list", () => $"{grabber.VisibleWindows().Count} windows");

        Check("transcription", () =>
        {
            var model = models.Find(settings.TranscriptionModelPath)
                        ?? throw new FileNotFoundException("no Whisper model: the release ships one in a models folder next to LocalTake");
            var error = (loadModel ?? WhisperTranscriber.CheckRuntime)(model);
            return error is null
                ? $"{model} ({new FileInfo(model).Length / (1024 * 1024)} MB), language {settings.TranscriptionLanguage}"
                : throw new InvalidOperationException($"{model} does not load: {error}");
        });

        Check("global shortcut", () =>
        {
            var parsed = Hotkey.TryParse(settings.HotkeyRegion, out var hotkey);
            var ok = parsed && hotkeys.Register(hotkey, () => { });
            hotkeys.UnregisterAll();
            return ok ? settings.HotkeyRegion + " accepted" : throw new InvalidOperationException(settings.HotkeyRegion + " refused by the system");
        });

        Line(failures == 0 ? "Everything LocalTake needs is available." : $"{failures} check(s) failed.");
        if (log.IsEnabled(LogLevel.Information))
            log.LogInformation("Diagnostics:\n{Report}", report.ToString());
        return failures == 0 ? 0 : 2;
    }
}
