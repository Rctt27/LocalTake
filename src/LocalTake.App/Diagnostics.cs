using System.Text;
using LocalTake.Core.Geometry;
using LocalTake.Core.Hotkeys;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalTake.App;

/// <summary>
/// "LocalTake --diagnostics": checks what LocalTake needs from this computer, without opening any window, and prints
/// the report (also written to the log). Meant to be attached to a bug report, on macOS especially.
/// </summary>
internal static class Diagnostics
{
    public static async Task<int> RunAsync(TextWriter output)
    {
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

        Line($"LocalTake {typeof(Diagnostics).Assembly.GetName().Version} — {Environment.OSVersion} — {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        using var services = Composition.Build();
        var paths = services.GetRequiredService<IAppPaths>();
        var settings = services.GetRequiredService<AppSettings>();
        Line($"Settings: {paths.SettingsFile}");
        Line($"Log:      {paths.LogsFolder}");
        Line($"Output:   {settings.OutputFolder}");

        var ffmpeg = services.GetRequiredService<FfmpegLocator>().Find(settings.FfmpegPath);
        var platform = services.GetRequiredService<IFfmpegPlatform>();
        Check("ffmpeg", () => ffmpeg ?? throw new FileNotFoundException(platform.InstallHint));
        if (ffmpeg is not null)
        {
            var devices = await services.GetRequiredService<IAudioDeviceLister>().ListAsync(ffmpeg);
            Check("audio inputs", () => devices.Count > 0 ? string.Join(", ", devices) : throw new InvalidOperationException("none listed"));
            Check("computer sound", () => devices.Contains(platform.SystemAudioDevice)
                ? platform.SystemAudioDevice
                : throw new InvalidOperationException("missing: " + platform.SystemAudioRequirement));
        }

        var grabber = services.GetRequiredService<IScreenGrabber>();
        Check("screen capture", () =>
        {
            var image = grabber.Grab(new PixelRect(0, 0, 64, 64));
            // A capture without the permission comes back uniform (the wallpaper only, or black).
            var distinct = Enumerable.Range(0, image.Width * image.Height).Select(i => BitConverter.ToInt32(image.Pixels, i * 4)).Distinct().Count();
            return $"{image.Width}×{image.Height}, {distinct} colours";
        });
        Check("window list", () => $"{grabber.VisibleWindows().Count} windows");

        var hotkeys = services.GetRequiredService<IGlobalHotkeys>();
        Check("global shortcut", () =>
        {
            var parsed = Hotkey.TryParse(settings.HotkeyRegion, out var hotkey);
            var ok = parsed && hotkeys.Register(hotkey, () => { });
            hotkeys.UnregisterAll();
            return ok ? settings.HotkeyRegion + " accepted" : throw new InvalidOperationException(settings.HotkeyRegion + " refused by the system");
        });

        Line(failures == 0 ? "Everything LocalTake needs is available." : $"{failures} check(s) failed.");
        var log = services.GetRequiredService<ILogger<App>>();
        if (log.IsEnabled(LogLevel.Information))
            log.LogInformation("Diagnostics:\n{Report}", report.ToString());
        return failures == 0 ? 0 : 2;
    }
}
