namespace LocalTake.Core.Recording;

/// <summary>The ffmpeg command line of a recording, whatever the platform's inputs.</summary>
public static class RecordingCommand
{
    /// <summary>ffmpeg prints this, at the info level, once every input is open and capturing.</summary>
    public const string CapturingLine = "Press [q] to stop";

    public static IReadOnlyList<string> Build(CaptureInputs inputs, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        // Each line tagged with its level: the info level announces when capture starts (CapturingLine).
        var args = new List<string> { "-hide_banner", "-loglevel", "level+info", "-nostats", "-y" };

        // Audio inputs, then the screen, then the trailing audio inputs: ffmpeg opens them in this order.
        var audioIndexes = new List<int>();
        var input = 0;
        foreach (var audio in inputs.AudioInputs)
        {
            audioIndexes.Add(input++);
            args.AddRange(audio);
        }
        var videoInput = input++;
        args.AddRange(inputs.VideoInput);
        foreach (var audio in inputs.TrailingAudioInputs ?? [])
        {
            audioIndexes.Add(input++);
            args.AddRange(audio);
        }

        switch (audioIndexes.Count)
        {
            case 1:
                args.AddRange(["-map", $"{videoInput}:v", "-map", $"{audioIndexes[0]}:a"]);
                break;
            case 2:
                args.AddRange(
                [
                    // amix divides each input by the number of inputs; volume=2 restores their level.
                    "-filter_complex",
                    $"[{audioIndexes[0]}:a][{audioIndexes[1]}:a]amix=inputs=2:duration=longest:dropout_transition=0,volume=2[a]",
                    "-map", $"{videoInput}:v", "-map", "[a]"
                ]);
                break;
        }
        if (inputs.VideoFilter is { } filter)
            args.AddRange(["-vf", filter]);

        // ultrafast keeps up in real time even on a large screen; yuv420p is what every player expects.
        // zerolatency: with frame threads, x264 holds about 0.35 s of frames, ffmpeg reads the audio that much
        // behind, and "q" discarded that audio: the last words were cut.
        args.AddRange(["-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-crf", "23", "-pix_fmt", "yuv420p"]);
        if (audioIndexes.Count > 0)
            args.AddRange(["-c:a", "aac", "-b:a", "160k"]);
        if (inputs.DownmixToStereo)
            args.AddRange(["-ac", "2"]);
        args.AddRange(["-movflags", "+faststart", outputPath]);
        return args;
    }
}
