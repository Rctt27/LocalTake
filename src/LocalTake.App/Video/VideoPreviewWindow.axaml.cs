using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LocalTake.App.Adapters;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;

namespace LocalTake.App.Video;

/// <summary>What the video preview needs from the rest of the app.</summary>
internal sealed record VideoPreviewContext(IVideoFrames Frames, FfmpegLocator Locator, Core.Settings.AppSettings Settings, IShell Shell, IFileTrash Trash, IUserDialogs Dialogs);

/// <summary>
/// The saved video, as in TinyTake: its frames along a seek bar, with a thumbnail while hovering it. Playing it is
/// left to the system's video player, which LocalTake does not bundle.
/// </summary>
#pragma warning disable CA1001 // The cancellation sources are disposed when the window closes.
public partial class VideoPreviewWindow : Window
#pragma warning restore CA1001
{
    private const double JumpSeconds = 10;

    private readonly VideoPreviewContext _context = null!;
    private readonly string _path = "";
    private readonly DispatcherTimer _seekDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private CancellationTokenSource? _frameLoad;
    private CancellationTokenSource? _thumbnailLoad;
    private VideoInfo? _info;
    private string? _ffmpeg;

    /// <summary>For the XAML previewer.</summary>
    public VideoPreviewWindow() => InitializeComponent();

    internal VideoPreviewWindow(string path, bool fullScreen, VideoPreviewContext context)
    {
        InitializeComponent();
        _path = path;
        _context = context;
        FullScreen = fullScreen;
        Icon = AppIcon.Window;
        Title = "LocalTake — " + Path.GetFileName(path);
        FileNameText.Text = path;

        PlayButton.Click += (_, _) => Play();
        RevealButton.Click += (_, _) => context.Shell.Reveal(path);
        DeleteButton.Click += async (_, _) => await DiscardAsync();
        RetakeButton.Click += (_, _) => RetakeRequested?.Invoke(this, EventArgs.Empty);
        SeekBar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                PositionText.Text = VideoInfo.Format(TimeSpan.FromSeconds(SeekBar.Value));
                _seekDelay.Stop();
                _seekDelay.Start();
            }
        };
        _seekDelay.Tick += async (_, _) =>
        {
            _seekDelay.Stop();
            await ShowFrameAsync(TimeSpan.FromSeconds(SeekBar.Value));
        };
        SeekBar.PointerMoved += async (_, e) => await ShowThumbnailAsync(e);
        SeekBar.PointerExited += (_, _) => ThumbnailPopup.IsOpen = false;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) =>
        {
            _frameLoad?.Cancel();
            _thumbnailLoad?.Cancel();
            _frameLoad?.Dispose();
            _thumbnailLoad?.Dispose();
            (Frame.Source as IDisposable)?.Dispose();
            (Thumbnail.Source as IDisposable)?.Dispose();
        };
    }

    /// <summary>"Refaire": the video goes to the Recycle Bin and a new one is recorded the same way.</summary>
    public event EventHandler? RetakeRequested;

    /// <summary>Whether the whole screen was recorded rather than an area.</summary>
    public bool FullScreen { get; }

    private async Task LoadAsync()
    {
        _ffmpeg = _context.Locator.Find(_context.Settings.FfmpegPath);
        _info = _ffmpeg is null ? null : await _context.Frames.ProbeAsync(_ffmpeg, _path);
        if (_info is null)
        {
            ErrorText.Text = "Aperçu impossible : ffmpeg ne peut pas lire cette vidéo. Elle s'ouvre peut-être dans un autre lecteur.";
            ErrorText.IsVisible = true;
            SeekBar.IsEnabled = false;
            return;
        }
        SeekBar.Maximum = Math.Max(0.1, _info.Duration.TotalSeconds);
        TotalText.Text = VideoInfo.Format(_info.Duration);
        await ShowFrameAsync(TimeSpan.Zero);
    }

    private async Task ShowFrameAsync(TimeSpan at)
    {
        if (_ffmpeg is null || _info is null)
            return;
        _frameLoad?.Cancel();
        _frameLoad = new CancellationTokenSource();
        var width = Math.Min(_info.Width, 1600);
        var frame = await _context.Frames.FrameAsync(_ffmpeg, _path, at, width, _info, _frameLoad.Token);
        if (frame is null || _frameLoad.IsCancellationRequested)
            return;
        var old = Frame.Source as IDisposable;
        Frame.Source = ImageData.ToBitmap(frame);
        old?.Dispose();
    }

    private async Task ShowThumbnailAsync(PointerEventArgs e)
    {
        if (_ffmpeg is null || _info is null || SeekBar.Bounds.Width <= 0)
            return;
        var ratio = Math.Clamp(e.GetPosition(SeekBar).X / SeekBar.Bounds.Width, 0, 1);
        var at = TimeSpan.FromSeconds(ratio * _info.Duration.TotalSeconds);
        ThumbnailTime.Text = VideoInfo.Format(at);
        ThumbnailPopup.HorizontalOffset = e.GetPosition(SeekBar).X - SeekBar.Bounds.Width / 2;
        ThumbnailPopup.IsOpen = true;

        _thumbnailLoad?.Cancel();
        _thumbnailLoad = new CancellationTokenSource();
        var frame = await _context.Frames.FrameAsync(_ffmpeg, _path, at, 200, _info, _thumbnailLoad.Token);
        if (frame is null || _thumbnailLoad.IsCancellationRequested)
            return;
        var old = Thumbnail.Source as IDisposable;
        Thumbnail.Source = ImageData.ToBitmap(frame);
        old?.Dispose();
    }

    private void Play()
    {
        try
        {
            _context.Shell.OpenFile(_path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _context.Dialogs.ShowError("Aucun lecteur vidéo n'a pu ouvrir le fichier : " + ex.Message);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                Play();
                break;
            case Key.Left:
                SeekBar.Value = Math.Max(0, SeekBar.Value - JumpSeconds);
                break;
            case Key.Right:
                SeekBar.Value = Math.Min(SeekBar.Maximum, SeekBar.Value + JumpSeconds);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Sends the video to the Recycle Bin and closes the preview.</summary>
    /// <returns>False when the file could not be moved.</returns>
    internal async Task<bool> DiscardAsync()
    {
        // ffmpeg may still be reading a frame of it.
        _frameLoad?.Cancel();
        _thumbnailLoad?.Cancel();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(_path))
                    _context.Trash.MoveToTrash(_path);
                Close();
                return true;
            }
            catch (IOException ex)
            {
                if (attempt >= 10)
                {
                    _context.Dialogs.ShowError("La vidéo n'a pas pu être envoyée à la Corbeille : " + ex.Message);
                    return false;
                }
                await Task.Delay(200);
            }
        }
    }
}
