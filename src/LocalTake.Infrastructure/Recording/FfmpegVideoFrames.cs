using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;

namespace LocalTake.Infrastructure.Recording;

/// <summary>Reads videos with ffmpeg: their length and size, and single frames for the preview.</summary>
public sealed class FfmpegVideoFrames : IVideoFrames
{
    public async Task<VideoInfo?> ProbeAsync(string ffmpegPath, string videoPath)
    {
        try
        {
            var (_, errors) = await RunAsync(ffmpegPath, ["-hide_banner", "-i", videoPath], CancellationToken.None);
            return VideoInfo.Parse(errors);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<PixelBuffer?> FrameAsync(
        string ffmpegPath, string videoPath, TimeSpan at, int width, VideoInfo info, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        // Even dimensions, as the scaler prefers them; the proportions of the video.
        var height = Math.Max(2, (int)Math.Round((double)width * info.Height / info.Width / 2) * 2);
        var seconds = Math.Max(0, at.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        string[] args =
        [
            "-hide_banner", "-loglevel", "error", "-ss", seconds, "-i", videoPath, "-frames:v", "1",
            "-vf", $"scale={width}:{height}", "-f", "rawvideo", "-pix_fmt", "bgra", "-"
        ];
        try
        {
            var (pixels, _) = await RunAsync(ffmpegPath, args, cancellation);
            if (pixels.Length != width * height * 4)
                return null;
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return new PixelBuffer(width, height, pixels);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<(byte[] Output, string Errors)> RunAsync(string ffmpegPath, IEnumerable<string> args, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        try
        {
            await process.WaitForExitAsync(cancellation);
            await copy;
            return (output.ToArray(), await errors);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
