using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using LocalTake.Core.Transcription;

namespace LocalTake.Infrastructure.Transcription;

/// <summary>Writes a video's sound for Whisper with the same ffmpeg that records the videos.</summary>
public sealed class FfmpegAudioExtractor(FfmpegLocator locator, AppSettings settings, IFfmpegPlatform platform) : IAudioExtractor
{
    public async Task ExtractAsync(string videoPath, string audioPath, CancellationToken cancellationToken)
    {
        var ffmpeg = locator.Find(settings.FfmpegPath)
                     ?? throw new InvalidOperationException("ffmpeg est introuvable : il sert à lire le son de la vidéo. " + platform.InstallHint);
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in AudioExtraction.Arguments(videoPath, audioPath))
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg n'a pas démarré.");
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Exited meanwhile.
            }
            throw;
        }
        if (process.ExitCode == 0)
            return;

        var error = (await errors).Trim();
        throw new InvalidOperationException(AudioExtraction.SaysNoSound(error)
            ? "La vidéo n'a pas de son à transcrire."
            : "ffmpeg n'a pas pu lire le son de la vidéo : " + error);
    }
}
