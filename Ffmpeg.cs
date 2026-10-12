using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LocalTake;

/// <summary>Locates ffmpeg and lists its audio inputs. LocalTake never downloads it.</summary>
internal static class Ffmpeg
{
    /// <summary>Audio driver installed with TinyTake ("TinyTake Filter"): it records what the computer plays.</summary>
    public const string SystemAudioDevice = "virtual-audio-capturer";

    public const string InstallHint = "Installez-le avec la commande « winget install Gyan.FFmpeg », " +
                                      "ou indiquez l'emplacement de ffmpeg.exe dans les réglages.";

    public static string? Find(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return File.Exists(configuredPath) ? configuredPath : null;

        var nextToApp = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(nextToApp))
            return nextToApp;

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in pathDirectories)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }
        return null;
    }

    /// <summary>DirectShow audio input names, as ffmpeg expects them after "audio=".</summary>
    public static List<string> ListAudioDevices(string ffmpegPath)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        // Read asynchronously so that the timeout applies even if a stuck audio driver keeps ffmpeg waiting.
        var reading = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Exited meanwhile.
            }
        }
        var output = reading.Wait(2000) ? reading.Result : "";

        // Recent builds tag each device: [dshow @ …] "Name" (audio).
        // Older ones list devices under a "DirectShow audio devices" heading, each followed by an "Alternative name" line.
        var devices = new List<string>();
        var inAudioSection = false;
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("DirectShow audio devices"))
                inAudioSection = true;
            else if (line.Contains("DirectShow video devices"))
                inAudioSection = false;

            var match = Regex.Match(line, "\"([^\"]+)\"\\s*(\\((audio|video|none)\\))?");
            if (!match.Success || line.Contains("Alternative name"))
                continue;
            var kind = match.Groups[3].Value;
            if (kind == "audio" || (kind == "" && inAudioSection))
                devices.Add(match.Groups[1].Value);
        }
        return devices.Distinct().ToList();
    }
}

internal sealed record VideoOptions(int FrameRate, string? Microphone, bool SystemAudio);

/// <summary>One ffmpeg process recording a screen area to an MP4 file.</summary>
internal sealed class VideoRecorder : IDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _log = new();
    private readonly Action<VideoRecorder> _onUnexpectedExit;
    private volatile bool _stopping;

    public string OutputPath { get; }

    public string ErrorTail => string.Join("\n", _log.ToArray().TakeLast(6));

    private VideoRecorder(Process process, string outputPath, Action<VideoRecorder> onUnexpectedExit)
    {
        _process = process;
        OutputPath = outputPath;
        _onUnexpectedExit = onUnexpectedExit;
    }

    /// <param name="area">Physical screen pixels, as everywhere else in LocalTake.</param>
    /// <param name="onUnexpectedExit">
    /// ffmpeg stopped on its own (busy microphone, full disk…). Called on a worker thread. Passed here rather than
    /// exposed as an event: ffmpeg can fail before a subscriber would be attached.
    /// </param>
    public static VideoRecorder Start(string ffmpegPath, Rectangle area, string outputPath, VideoOptions options,
        Action<VideoRecorder> onUnexpectedExit)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in BuildArguments(area, outputPath, options))
            start.ArgumentList.Add(arg);

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var recorder = new VideoRecorder(process, outputPath, onUnexpectedExit);
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            recorder._log.Enqueue(e.Data);
            while (recorder._log.Count > 50)
                recorder._log.TryDequeue(out string? _);
        };
        process.Exited += (_, _) =>
        {
            if (recorder._stopping)
                return;
            // Exited can come before the last stderr lines, which usually hold the reason; this waits for them.
            process.WaitForExit();
            recorder._onUnexpectedExit(recorder);
        };

        process.Start();
        process.BeginErrorReadLine();
        return recorder;
    }

    private static List<string> BuildArguments(Rectangle area, string outputPath, VideoOptions options)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-y" };

        // Audio inputs first, so that the screen is always the last input.
        var audioInputs = new List<string>();
        if (!string.IsNullOrEmpty(options.Microphone))
            audioInputs.Add(options.Microphone);
        if (options.SystemAudio)
            audioInputs.Add(Ffmpeg.SystemAudioDevice);
        foreach (var device in audioInputs)
            args.AddRange(new[] { "-f", "dshow", "-thread_queue_size", "1024", "-audio_buffer_size", "50", "-i", "audio=" + device });

        // gdigrab takes physical coordinates on the virtual desktop, like the rest of LocalTake.
        args.AddRange(new[]
        {
            "-f", "gdigrab", "-thread_queue_size", "1024", "-framerate", Invariant(options.FrameRate),
            "-offset_x", Invariant(area.X), "-offset_y", Invariant(area.Y),
            "-video_size", $"{Invariant(area.Width)}x{Invariant(area.Height)}", "-draw_mouse", "1", "-i", "desktop"
        });

        var videoInput = audioInputs.Count;
        switch (audioInputs.Count)
        {
            case 1:
                args.AddRange(new[] { "-map", $"{videoInput}:v", "-map", "0:a" });
                break;
            case 2:
                args.AddRange(new[]
                {
                    // amix divides each input by the number of inputs; volume=2 restores their level.
                    "-filter_complex", "[0:a][1:a]amix=inputs=2:duration=longest:dropout_transition=0,volume=2[a]",
                    "-map", $"{videoInput}:v", "-map", "[a]"
                });
                break;
        }

        // ultrafast keeps up in real time even on a large screen; yuv420p is what every player expects.
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-crf", "23", "-pix_fmt", "yuv420p" });
        if (audioInputs.Count > 0)
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "160k" });
        args.AddRange(new[] { "-movflags", "+faststart", outputPath });
        return args;
    }

    /// <summary>Some cultures write negative numbers with a Unicode minus sign, which ffmpeg rejects.</summary>
    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Asks ffmpeg to finish the file; true when a playable file was written.</summary>
    public async Task<bool> StopAsync()
    {
        _stopping = true;
        try
        {
            // "q" on stdin is ffmpeg's clean stop: it writes the MP4 index, unlike killing the process.
            await _process.StandardInput.WriteAsync('q');
            await _process.StandardInput.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Already exited.
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Kill();
            return false;
        }
        return _process.ExitCode == 0 && File.Exists(OutputPath) && new FileInfo(OutputPath).Length > 0;
    }

    /// <summary>Stops immediately; the file is left incomplete.</summary>
    public void Kill()
    {
        _stopping = true;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already exited.
        }
    }

    public void Dispose() => _process.Dispose();
}
