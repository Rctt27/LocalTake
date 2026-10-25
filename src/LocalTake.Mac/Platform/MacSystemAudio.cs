using CoreFoundation;
using CoreMedia;
using Foundation;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Platform.MacOS;
using Microsoft.Extensions.Logging;
using ScreenCaptureKit;

namespace LocalTake.Mac.Platform;

/// <summary>
/// What the Mac plays, captured by ScreenCaptureKit and written to a FIFO for ffmpeg: no audio driver to install.
/// Replaces BlackHole, as WASAPI loopback replaced TinyTake's driver on Windows.
/// </summary>
internal sealed class MacSystemAudio(ILogger<MacSystemAudio> log) : ISystemAudioSource
{
    /// <summary>ScreenCaptureKit captures audio from macOS 13; LocalTake needs 14.</summary>
    public bool IsAvailable() => true;

    public ISystemAudioCapture Start() => new MacSystemAudioCapture(log);
}

/// <summary>One recording's capture: ScreenCaptureKit starts when ffmpeg opens the FIFO, and stops on disposal.</summary>
internal sealed class MacSystemAudioCapture : ISystemAudioCapture
{
    /// <summary>The format asked of ScreenCaptureKit and declared to ffmpeg.</summary>
    private static readonly RawAudioFormat Format = new("f32le", 48000, 2);

    private readonly ILogger _log;
    private readonly MacAudioPipe _pipe;
    private readonly SampleOutput _output;
    private readonly Lock _gate = new();
    private SCStream? _stream;
    private bool _disposed;

    public MacSystemAudioCapture(ILogger log)
    {
        _log = log;
        // ScreenCaptureKit stamps its samples on the host clock.
        _pipe = new MacAudioPipe(Path.GetTempPath(), Format, () => TimeSpan.FromSeconds(CMClock.HostTimeClock.CurrentTime.Seconds));
        _output = new SampleOutput(_pipe, log);
        _pipe.Opened += StartStream;
    }

    public PipedAudio Input => _pipe.Input;

    /// <summary>
    /// On the pipe's thread, once ffmpeg has opened it. Should the capture fail (permission withdrawn…), the pipe goes on
    /// with silence: the video is recorded, with the Mac's sound muted.
    /// </summary>
    private void StartStream()
    {
        SCShareableContent.GetShareableContent(true, true, (content, error) =>
        {
            if (content?.Displays.FirstOrDefault() is not { } display)
            {
                Failed("no display to capture the sound of", error);
                return;
            }
            using var filter = new SCContentFilter(display, [], SCContentFilterOption.Exclude);
            using var configuration = new SCStreamConfiguration
            {
                CapturesAudio = true,
                SampleRate = Format.SampleRate,
                ChannelCount = Format.Channels,
                ExcludesCurrentProcessAudio = true,
                // The picture is not used: the smallest, once a second.
                Width = 2,
                Height = 2,
                MinimumFrameInterval = new CMTime(1, 1),
                ShowsCursor = false
            };
            lock (_gate)
            {
                if (_disposed)
                    return;
                var stream = new SCStream(filter, configuration, null);
                var queue = new DispatchQueue("io.github.rctt27.localtake.sound");
                // A screen output too, which drops the frames: without one, ScreenCaptureKit logs each frame it drops.
                if (!stream.AddStreamOutput(_output, SCStreamOutputType.Audio, queue, out var audioError) ||
                    !stream.AddStreamOutput(_output, SCStreamOutputType.Screen, queue, out audioError))
                {
                    Failed("the stream refused its outputs", audioError);
                    stream.Dispose();
                    return;
                }
                _stream = stream;
                stream.StartCapture(startError =>
                {
                    if (startError is not null)
                        Failed("the capture did not start", startError);
                });
            }
        });
    }

    private void Failed(string what, NSError? error)
    {
        if (_log.IsEnabled(LogLevel.Warning))
            _log.LogWarning("The Mac's sound is recorded silent: {What} ({Error})", what, error?.LocalizedDescription);
    }

    public void Dispose()
    {
        SCStream? stream;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            stream = _stream;
            _stream = null;
        }
        if (stream is not null)
        {
            using var stopped = new ManualResetEventSlim();
            stream.StopCapture(_ => stopped.Set());
            stopped.Wait(TimeSpan.FromSeconds(1));
            stream.Dispose();
        }
        _pipe.Dispose();
        _output.Dispose();
    }

    /// <summary>Receives ScreenCaptureKit's samples, on its queue, and writes the audio ones to the pipe.</summary>
    private sealed class SampleOutput(MacAudioPipe pipe, ILogger log) : NSObject, ISCStreamOutput
    {
        private bool _unexpectedFormatLogged;

        [Export("stream:didOutputSampleBuffer:ofType:")]
        public void DidOutputSampleBuffer(SCStream stream, CMSampleBuffer sampleBuffer, SCStreamOutputType type)
        {
            if (type != SCStreamOutputType.Audio || !sampleBuffer.IsValid || sampleBuffer.NumSamples <= 0)
                return;
            if (sampleBuffer.GetAudioFormatDescription()?.AudioStreamBasicDescription is not { } format ||
                format.BitsPerChannel != 32 || (int)format.SampleRate != Format.SampleRate || format.ChannelsPerFrame != Format.Channels)
            {
                if (!_unexpectedFormatLogged && log.IsEnabled(LogLevel.Warning))
                    log.LogWarning("Unexpected audio format from ScreenCaptureKit: the Mac's sound is skipped");
                _unexpectedFormatLogged = true;
                return;
            }
            if (sampleBuffer.GetDataBuffer() is not { } data)
                return;

            var bytes = new byte[(int)data.DataLength];
            data.CopyDataBytes(0, data.DataLength, bytes);
            // Non-interleaved: one channel after the other in the block buffer.
            var planar = (format.FormatFlags & AudioToolbox.AudioFormatFlags.IsNonInterleaved) != 0;
            var pcm = planar ? PlanarAudio.Interleave(Split(bytes, format.ChannelsPerFrame)) : bytes;
            pipe.Write(TimeSpan.FromSeconds(sampleBuffer.PresentationTimeStamp.Seconds), pcm);
        }

        private static byte[][] Split(byte[] bytes, int channels)
        {
            var size = bytes.Length / channels;
            return [.. Enumerable.Range(0, channels).Select(channel => bytes.AsSpan(channel * size, size).ToArray())];
        }
    }
}
