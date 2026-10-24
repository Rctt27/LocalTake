using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using LocalTake.Core.Geometry;
using LocalTake.Core.Recording;
using LocalTake.Infrastructure.Platform;
using LocalTake.Infrastructure.Recording;
using LocalTake.Platform.Windows;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalTake.Windows.Tests.Recording;

/// <summary>
/// A window at the top left of the main screen turns from black to white while a beep plays, recorded as the app
/// records: in the video, the beep must start when the picture turns white. Needs ffmpeg and an audio output.
/// </summary>
[Collection(ScreenRecordings.Name)]
public sealed partial class SyncTests : IDisposable
{
    private static readonly PixelRect Area = new(0, 0, 320, 240);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"localtake-sync-{Guid.NewGuid():N}.mp4");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task The_computer_sound_stays_in_sync_with_the_picture()
    {
        var ffmpeg = AppPaths.CreateFfmpegLocator(new FileSystem()).Find("");
        var source = new WasapiSystemAudio();
        Assert.SkipUnless(ffmpeg is not null && source.IsAvailable(), "Needs ffmpeg and an audio output.");

        var window = await Sta.Run(() =>
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Left = 0,
                Top = 0,
                Width = 320,
                Height = 240,
                Background = Brushes.Black,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            w.Show();
            return w;
        });
        using var beep = new WasapiOut(AudioClientShareMode.Shared, true, 20);
        beep.Init(new SignalGenerator(48000, 2) { Frequency = 1000, Gain = 0.5 }.Take(TimeSpan.FromMilliseconds(400)));

        try
        {
            var factory = new FfmpegVideoRecorderFactory(new DirectShowGdigrab(source.IsAvailable), source);
            using (var recorder = factory.Start(ffmpeg!, Area, _path, new VideoOptions(30, null, true), _ => { }))
            {
                (await recorder.Capturing.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)).ShouldBeTrue(recorder.ErrorTail);
                await Task.Delay(1500, TestContext.Current.CancellationToken);
                await Sta.Run(() =>
                {
                    window.Background = Brushes.White;
                    Sta.DoEvents();
                });
                beep.Play();
                await Task.Delay(1500, TestContext.Current.CancellationToken);
                (await recorder.StopAsync()).ShouldBeTrue(recorder.ErrorTail);
            }
        }
        finally
        {
            await Sta.Run(window.Close);
        }

        var flash = await FirstWhiteFrameAsync(ffmpeg!);
        var sound = await FirstSoundAsync(ffmpeg!);
        TestContext.Current.TestOutputHelper?.WriteLine($"flash {flash:F3} s, beep {sound:F3} s, beep - flash {sound - flash:F3} s");
        (sound - flash).ShouldBeInRange(-0.1, 0.1);
    }

    private async Task<double> FirstWhiteFrameAsync(string ffmpeg)
    {
        var output = await RunAsync(ffmpeg, "-v", "error", "-i", _path, "-vf",
            "signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=-", "-f", "null", "-");
        double? time = null;
        foreach (var line in output.Split('\n'))
        {
            if (PtsTime().Match(line) is { Success: true } pts)
                time = double.Parse(pts.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Luma().Match(line) is { Success: true } y && double.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture) > 128)
                return time!.Value;
        }
        throw new InvalidOperationException("No white frame in the recording.");
    }

    private async Task<double> FirstSoundAsync(string ffmpeg)
    {
        var output = await RunAsync(ffmpeg, "-hide_banner", "-i", _path, "-vn", "-af", "silencedetect=noise=-30dB:d=0.2", "-f", "null", "-");
        var end = SilenceEnd().Match(output);
        end.Success.ShouldBeTrue("No beep in the recording:\n" + output);
        return double.Parse(end.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static async Task<string> RunAsync(string ffmpeg, params string[] args)
    {
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output + await error;
    }

    [GeneratedRegex(@"pts_time:([\d.]+)")]
    private static partial Regex PtsTime();

    [GeneratedRegex(@"YAVG=([\d.]+)")]
    private static partial Regex Luma();

    [GeneratedRegex(@"silence_end: ([\d.]+)")]
    private static partial Regex SilenceEnd();
}
