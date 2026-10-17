using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;

namespace LocalTake.Infrastructure.Recording;

/// <summary>Asks ffmpeg for the audio inputs it can record.</summary>
public sealed class FfmpegAudioDeviceLister(IFfmpegPlatform platform) : IAudioDeviceLister
{
    public async Task<IReadOnlyList<string>> ListAsync(string ffmpegPath)
    {
        try
        {
            return await Task.Run(() => List(ffmpegPath));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return [];
        }
    }

    private IReadOnlyList<string> List(string ffmpegPath)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var arg in platform.ListDevicesArguments)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start.");
        // Read asynchronously so that the timeout applies even if a stuck audio driver keeps ffmpeg waiting.
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Exited meanwhile.
            }
        }
        var text = (errors.Wait(2000) ? errors.Result : "") + (output.Wait(2000) ? output.Result : "");
        return platform.ParseDeviceList(text);
    }
}
