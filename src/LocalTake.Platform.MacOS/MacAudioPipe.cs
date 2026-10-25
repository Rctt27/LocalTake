using System.Runtime.InteropServices;
using System.Threading.Channels;
using LocalTake.Core.Recording;

namespace LocalTake.Platform.MacOS;

/// <summary>
/// The FIFO through which ffmpeg reads the Mac's sound, as the named pipe of <c>WasapiSystemAudio</c> on Windows. Its
/// timeline starts when ffmpeg opens it: sound written before is dropped, and while no sound comes the stream is kept
/// level with the clock by silence (<see cref="AudioTimeline"/>), so the track lasts as long as the video.
/// </summary>
public sealed class MacAudioPipe : IDisposable
{
    private static readonly TimeSpan PaddingInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>rw-------: the pipe is the user's alone.</summary>
    private const ushort OwnerOnly = 0x180;

    private readonly RawAudioFormat _format;
    private readonly Func<TimeSpan> _clock;
    private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Lock _gate = new();
    private readonly Task _serving;
    private AudioTimeline? _timeline;
    private Timer? _padding;
    private FileStream? _stream;
    private bool _disposed;

    /// <param name="directory">Where the FIFO is made; it is removed on disposal.</param>
    /// <param name="clock">The clock the sound's times are on (the host clock, for ScreenCaptureKit).</param>
    public MacAudioPipe(string directory, RawAudioFormat format, Func<TimeSpan> clock)
    {
        _format = format;
        _clock = clock;
        var path = Path.Combine(directory, $"localtake-audio-{Environment.ProcessId}-{Guid.NewGuid():N}.pcm");
        if (Libc.mkfifo(path, OwnerOnly) != 0)
            throw new IOException($"The audio pipe {path} could not be made: error {Marshal.GetLastPInvokeError()}.");
        Input = new PipedAudio(path, format);
        _serving = Task.Factory.StartNew(Serve, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public PipedAudio Input { get; }

    /// <summary>Packets whose time was too far from the stream's to be believed (<see cref="AudioTimeline.Reanchors"/>).</summary>
    public int Reanchors
    {
        get
        {
            lock (_gate)
                return _timeline?.Reanchors ?? 0;
        }
    }

    /// <summary>Raised on a background thread when ffmpeg opens the FIFO: the moment the capture should start.</summary>
    public event Action? Opened;

    /// <summary>Sound captured at <paramref name="at"/>. Ignored before ffmpeg opens the FIFO and after disposal.</summary>
    public void Write(TimeSpan at, ReadOnlySpan<byte> pcm)
    {
        lock (_gate)
        {
            if (_disposed || _timeline is null)
                return;
            var bytes = _timeline.Place(at, pcm);
            if (bytes.Length > 0)
                _chunks.Writer.TryWrite(bytes);
        }
    }

    private void Serve()
    {
        FileStream stream;
        try
        {
            // Blocks until ffmpeg opens the other end.
            stream = new FileStream(Input.PipePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                stream.Dispose();
                return;
            }
            _stream = stream;
            _timeline = new AudioTimeline(_format, _clock());
            _padding = new Timer(_ => Pad(), null, PaddingInterval, PaddingInterval);
        }
        // Each handler on its own: one that fails must neither stop the others nor this thread, or ffmpeg would wait on
        // an empty pipe. The padding then keeps the track silent.
        foreach (var handler in Opened?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)handler)();
            }
#pragma warning disable CA1031 // Whatever the handler threw, the sound must keep flowing to ffmpeg.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        try
        {
            while (_chunks.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_chunks.Reader.TryRead(out var chunk))
                    stream.Write(chunk);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // ffmpeg closed its end (stop, crash, kill), or this pipe was disposed.
        }
    }

    private void Pad()
    {
        lock (_gate)
        {
            if (_disposed || _timeline is null)
                return;
            var silence = _timeline.PadTo(_clock());
            if (silence.Length > 0)
                _chunks.Writer.TryWrite(silence);
        }
    }

    /// <summary>Closes the FIFO, which ends ffmpeg's input, and removes it.</summary>
    public void Dispose()
    {
        bool opened;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _padding?.Dispose();
            opened = _stream is not null;
        }
        _chunks.Writer.TryComplete();
        if (!opened)
            ReleaseWaitingOpen();
        _serving.Wait(TimeSpan.FromSeconds(1));
        _stream?.Dispose();
        File.Delete(Input.PipePath);
    }

    /// <summary>
    /// ffmpeg never opened the FIFO (it failed first): opening the reading end lets the blocked open of the writing end
    /// return, and it is kept open until that thread has seen the disposal.
    /// </summary>
    private void ReleaseWaitingOpen()
    {
        var reader = Libc.open(Input.PipePath, Libc.O_RDONLY | Libc.O_NONBLOCK);
        try
        {
            _serving.Wait(TimeSpan.FromSeconds(1));
        }
        finally
        {
            if (reader >= 0)
                _ = Libc.close(reader);
        }
    }
}
