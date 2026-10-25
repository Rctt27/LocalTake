using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Transcription;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>
/// Opens when a recording is saved, as in TinyTake, and plays it straight away: seek bar with a frame preview on
/// hover, 5-second jumps, replay at the end. The video is already in the output folder; the window only shows it,
/// sends it to the Recycle Bin, or asks for a new take. With a transcription, its subtitles are at the left, following
/// the video and editable (<see cref="TranscriptPanel"/>).
/// </summary>
public partial class VideoPreviewWindow : Window
{
    private readonly string _path;

    /// <summary>"Refaire": the owner checks that a recording can start, then calls <see cref="DiscardAsync"/>.</summary>
    public event Action? RetakeRequested;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool _playing;
    private bool _failed;
    private bool _sized;
    private bool _dragging;
    private bool _discarding;

    /// <summary>Set while the timer moves the seek bar, so that only the user's moves seek.</summary>
    private bool _updatingSeekBar;

    private readonly IShell _shell;
    private readonly IFileTrash _trash;
    private bool _thumbnailRequested;
    private bool _thumbnailFailed;
    private double _thumbnailSeconds = -1;

    /// <summary>The microphone track recorded with the video, kept for "Générer les sous-titres"; deleted on closing.</summary>
    private string? _microphoneTrack;
    private readonly Transcriptions _transcriptions;
    private bool _transcriptShown;

    /// <param name="fullScreen">How the video was recorded, which "Refaire" repeats.</param>
    /// <param name="transcription">The transcription of its microphone, or null to show the .srt next to it, if any.</param>
    /// <param name="microphoneTrack">
    /// Without a transcription, the microphone track recorded with the video, which "Générer les sous-titres" uses.
    /// </param>
    internal VideoPreviewWindow(string path, bool fullScreen, IShell shell, IFileTrash trash, SubtitleFile subtitles,
        Transcriptions transcriptions, TranscriptionJob? transcription, string? microphoneTrack = null)
    {
        InitializeComponent();
        _shell = shell;
        _trash = trash;
        _transcriptions = transcriptions;
        _microphoneTrack = microphoneTrack;
        Icon = AppIcon.ImageSource;
        _path = path;
        Title = "LocalTake — " + Path.GetFileName(path);
        FileNameText.Text = path;
        RetakeButton.ToolTip = "Envoyer cette vidéo à la Corbeille et en enregistrer une nouvelle " +
                               (fullScreen ? "de tout l'écran" : "(zone à sélectionner de nouveau)");
        _timer.Tick += (_, _) => ShowPosition();
        SeekBar.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
        SeekBar.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _dragging = false));
        Transcript.Load(path, subtitles, transcriptions, transcription);
        if (Transcript.HasSubtitles)
            ShowTranscript();
        Transcript.SubtitlesChanged += UpdateGenerateButton;
        UpdateGenerateButton();
        Transcript.SeekRequested += time => SeekTo(time.TotalSeconds);
        Transcript.EditingStarted += PauseIfPlaying;
        Transcript.CueShown += text =>
        {
            SubtitleText.Text = text ?? "";
            SubtitleBand.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        };
        VideoHost.SizeChanged += (_, _) => PlaceSubtitle();

        OpenMedia();
        Loaded += (_, _) => Play();
        Closed += (_, _) =>
        {
            ReleaseMedia();
            Transcript.Close(discard: false);
            // Never used: the subtitles were not asked for.
            if (_microphoneTrack is not null)
                _transcriptions.DiscardTrack(_microphoneTrack);
        };
    }

    private void UpdateGenerateButton()
    {
        GenerateButton.Visibility = Transcript.CanGenerate ? Visibility.Visible : Visibility.Collapsed;
        GenerateButton.IsEnabled = _transcriptions.CanTranscribe;
        GenerateButton.ToolTip = !GenerateButton.IsEnabled ? "Modèle de transcription introuvable : voir les réglages."
            : _microphoneTrack is not null ? "Transcrire le micro de cette vidéo, sur cet ordinateur"
            : "Transcrire le son de cette vidéo, sur cet ordinateur";
    }

    /// <summary>The track goes to the transcription, which deletes it once done.</summary>
    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var track = _microphoneTrack;
        _microphoneTrack = null;
        Transcript.Generate(track);
        ShowTranscript();
    }

    private void ShowTranscript()
    {
        if (_transcriptShown)
            return;
        _transcriptShown = true;
        TranscriptColumn.Width = new GridLength(340);
        TranscriptColumn.MinWidth = 220;
        Transcript.Visibility = TranscriptSplitter.Visibility = Visibility.Visible;
        // The highlighted subtitle follows the video closely.
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        Width += 340;
    }

    /// <summary>A new recording must not capture this window's sound.</summary>
    public void PauseIfPlaying()
    {
        if (_playing)
            Pause();
    }

    // ---- Playback -------------------------------------------------------------------------------------------

    private void OpenMedia()
    {
        var uri = new Uri(_path);
        Player.Source = uri;
        ThumbnailPlayer.Source = uri;
        _thumbnailRequested = false;
        _thumbnailSeconds = -1;
    }

    private void Play()
    {
        if (_failed)
            return;
        ReplayButton.Visibility = Visibility.Collapsed;
        Player.Play();
        _playing = true;
        _timer.Start();
        PlayPauseGlyph.Text = "";
    }

    private void Pause()
    {
        Player.Pause();
        _playing = false;
        _timer.Stop();
        ShowPosition();
        PlayPauseGlyph.Text = "";
    }

    private void TogglePlay()
    {
        if (_failed)
            return;
        if (_playing)
            Pause();
        else
            Play();
        Flash(_playing ? "" : "");
    }

    private void Flash(string glyph)
    {
        FlashGlyph.Text = glyph;
        FlashBadge.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.8)) { EasingFunction = new QuadraticEase() });
    }

    private void SeekTo(double seconds)
    {
        if (_failed)
            return;
        seconds = Math.Clamp(seconds, 0, SeekBar.Maximum);
        ReplayButton.Visibility = Visibility.Collapsed;
        Player.Position = TimeSpan.FromSeconds(seconds);
        SetSeekBar(seconds);
    }

    private void ShowPosition()
    {
        if (!_dragging)
            SetSeekBar(Player.Position.TotalSeconds);
    }

    private void SetSeekBar(double seconds)
    {
        _updatingSeekBar = true;
        SeekBar.Value = seconds;
        _updatingSeekBar = false;
        ElapsedText.Text = Format(seconds);
        Transcript.ShowPosition(TimeSpan.FromSeconds(seconds));
    }

    private static string Format(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            SeekBar.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
            TotalText.Text = Format(SeekBar.Maximum);
            Transcript.SetDuration(Player.NaturalDuration.TimeSpan);
        }
        PlaceSubtitle();
        SeekBar.SmallChange = 1;
        SeekBar.LargeChange = PlayerSeek.Step.TotalSeconds;
        if (!_sized)
        {
            _sized = true;
            FitToVideo();
        }
    }

    /// <summary>As TinyTake does, the end goes back to the start and offers to replay.</summary>
    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        Pause();
        Player.Position = TimeSpan.Zero;
        SetSeekBar(0);
        ReplayButton.Visibility = Visibility.Visible;
    }

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _failed = true;
        _playing = false;
        _timer.Stop();
        ReplayButton.Visibility = Visibility.Collapsed;
        ErrorText.Text = "L'aperçu ne peut pas lire cette vidéo, qui est pourtant enregistrée.\n\n" + e.ErrorException.Message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Puts the subtitle near the bottom of the picture, sized to it (<see cref="SubtitlePlacement"/>).</summary>
    private void PlaceSubtitle()
    {
        double width = VideoHost.ActualWidth, height = VideoHost.ActualHeight;
        if (width <= 0 || height <= 0)
            return;
        var placement = SubtitlePlacement.For(width, height, Player.NaturalVideoWidth, Player.NaturalVideoHeight);
        SubtitleText.FontSize = placement.FontSize;
        SubtitleBand.MaxWidth = placement.MaxWidth;
        SubtitleBand.Margin = new Thickness(0, 0, 0, placement.BottomMargin);
    }

    /// <summary>
    /// Sizes the window to the video at one video pixel per screen pixel, within 85 % of the monitor, keeping the
    /// window centred where it opened.
    /// </summary>
    private void FitToVideo()
    {
        if (Player.NaturalVideoWidth <= 0 || Player.NaturalVideoHeight <= 0)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var videoWidth = Player.NaturalVideoWidth / dpi.DpiScaleX;
        var videoHeight = Player.NaturalVideoHeight / dpi.DpiScaleY;
        var chromeWidth = ActualWidth - VideoHost.ActualWidth;
        var chromeHeight = ActualHeight - VideoHost.ActualHeight;
        var scale = Math.Min(1, Math.Min(
            (work.Width / dpi.DpiScaleX * 0.85 - chromeWidth) / videoWidth,
            (work.Height / dpi.DpiScaleY * 0.85 - chromeHeight) / videoHeight));

        var width = Math.Max(MinWidth, videoWidth * scale + chromeWidth);
        var height = Math.Max(MinHeight, videoHeight * scale + chromeHeight);
        Left -= (width - ActualWidth) / 2;
        Top -= (height - ActualHeight) / 2;
        Width = width;
        Height = height;
    }

    // ---- Seek bar and frame preview -------------------------------------------------------------------------

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updatingSeekBar)
            SeekTo(e.NewValue);
    }

    private void SeekBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_failed || _thumbnailFailed || SeekBar.Maximum <= 0 ||
            SeekBar.Template.FindName("PART_Track", SeekBar) is not Track track)
            return;

        var seconds = Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), 0, SeekBar.Maximum);
        ThumbnailTime.Text = Format(seconds);
        // Every mouse move would otherwise queue a decode.
        if (Math.Abs(seconds - _thumbnailSeconds) >= 0.1)
        {
            _thumbnailSeconds = seconds;
            ThumbnailPlayer.Position = TimeSpan.FromSeconds(seconds);
        }
        ThumbnailPopup.HorizontalOffset = e.GetPosition(SeekBar).X - (ThumbnailPlayer.Width + 6) / 2;
        ThumbnailPopup.IsOpen = true;
        if (!_thumbnailRequested)
        {
            // With a manual loaded behaviour, the media only opens on Play or Pause.
            _thumbnailRequested = true;
            ThumbnailPlayer.Pause();
        }
    }

    private void SeekBar_MouseLeave(object sender, MouseEventArgs e) => ThumbnailPopup.IsOpen = false;

    private void ThumbnailPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (ThumbnailPlayer.NaturalVideoWidth > 0)
            ThumbnailPlayer.Height = Math.Round(ThumbnailPlayer.Width * ThumbnailPlayer.NaturalVideoHeight / ThumbnailPlayer.NaturalVideoWidth);
    }

    private void ThumbnailPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _thumbnailFailed = true;
        ThumbnailPopup.IsOpen = false;
    }

    // ---- Buttons and keyboard -------------------------------------------------------------------------------

    private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlay();
    private void Rewind_Click(object sender, RoutedEventArgs e) => JumpBack();
    private void Forward_Click(object sender, RoutedEventArgs e) => JumpForward();

    private void JumpBack() => SeekTo(PlayerSeek.Back(Player.Position).TotalSeconds);

    private void JumpForward() => SeekTo(PlayerSeek.Forward(Player.Position, TimeSpan.FromSeconds(SeekBar.Maximum)).TotalSeconds);

    private void Video_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TogglePlay();

    private void Replay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Play();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // In a subtitle field, keys type text: Space, the arrows and Escape belong to it.
        if (Transcript.IsEditing)
            return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y)
        {
            e.Handled = e.Key == Key.Z ? Transcript.Undo() : Transcript.Redo();
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None)
            return;
        switch (e.Key)
        {
            case Key.Space or Key.K:
                TogglePlay();
                break;
            case Key.Left:
                JumpBack();
                break;
            case Key.Right:
                JumpForward();
                break;
            case Key.Home:
                SeekTo(0);
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_path))
            _shell.Reveal(_path);
        else
            MessageBox.Show(this, "La vidéo n'est plus à cet emplacement.", "LocalTake", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenExternally_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _shell.OpenFile(_path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossible d'ouvrir la vidéo : " + ex.Message, "LocalTake", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- Deleting and closing -------------------------------------------------------------------------------

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, $"Envoyer « {Path.GetFileName(_path)} » à la Corbeille ?", "LocalTake",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            await DiscardAsync();
    }

    /// <summary>No confirmation: the video goes to the Recycle Bin, where it can still be restored.</summary>
    private void Retake_Click(object sender, RoutedEventArgs e) => RetakeRequested?.Invoke();

    /// <summary>
    /// Sends the video to the Recycle Bin and closes the window. Returns false, with the video still shown, if the
    /// file could not be deleted.
    /// </summary>
    internal async Task<bool> DiscardAsync()
    {
        // A second click while the file is being released must not start a second deletion.
        if (_discarding)
            return false;
        _discarding = true;
        try
        {
            if (_playing)
                Pause();
            // The players keep the file open; closing them releases it, though not always at once.
            ReleaseMedia();
            for (var attempt = 0; attempt < 10 && IsLocked(_path); attempt++)
                await Task.Delay(100);

            try
            {
                if (File.Exists(_path))
                    _trash.MoveToTrash(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                MessageBox.Show(this, "La vidéo n'a pas pu être supprimée : " + ex.Message, "LocalTake",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                OpenMedia();
                Player.Pause();
                return false;
            }
            // The subtitles go with the video; the transcription stops, and its track is deleted.
            Transcript.Close(discard: true);
            TrashSubtitles();
            Close();
            return true;
        }
        finally
        {
            _discarding = false;
        }
    }

    private void TrashSubtitles()
    {
        var subtitles = SubtitleFile.PathFor(_path);
        try
        {
            if (File.Exists(subtitles))
                _trash.MoveToTrash(subtitles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // The video is gone; a .srt left behind is harmless.
        }
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var _ = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private void ReleaseMedia()
    {
        _timer.Stop();
        _playing = false;
        ThumbnailPopup.IsOpen = false;
        Player.Close();
        Player.Source = null;
        ThumbnailPlayer.Close();
        ThumbnailPlayer.Source = null;
    }
}
