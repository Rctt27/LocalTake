using AppKit;
using AVFoundation;
using AVKit;
using CoreGraphics;
using Foundation;
using LocalTake.Core.Ports;
using LocalTake.Mac.Adapters;

namespace LocalTake.Mac.Video;

/// <summary>
/// A saved video, played in the window with macOS's own player (frame previews on hover over the timeline, full
/// screen, Space to play), with the file's actions: show it in the Finder, record again, or send it to the Trash.
/// </summary>
internal sealed class VideoPreviewWindowController : NSWindowController
{
    private readonly string _path;
    private readonly IShell _shell;
    private readonly IFileTrash _trash;
    private readonly AVPlayerView _playerView;
    private AVPlayer? _player;

    public VideoPreviewWindowController(string path, IShell shell, IFileTrash trash) : base(CreateWindow(path))
    {
        _path = path;
        _shell = shell;
        _trash = trash;
        var window = Window!;

        _playerView = new AVPlayerView { ControlsStyle = AVPlayerViewControlsStyle.Floating, ShowsFullScreenToggleButton = true };
        Load();

        var name = NSTextField.CreateLabel(path);
        name.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
        name.TextColor = NSColor.SecondaryLabel;
        name.SetContentCompressionResistancePriority(1, NSLayoutConstraintOrientation.Horizontal);
        var reveal = Button("Afficher dans le Finder", "folder", "Ouvrir le dossier avec la vidéo sélectionnée", Reveal);
        var retake = Button("Refaire", "arrow.counterclockwise", "Mettre cette vidéo à la Corbeille et en enregistrer une autre de la même façon",
            () => RetakeRequested?.Invoke(this, EventArgs.Empty));
        var delete = Button("Supprimer", "trash", "Envoyer la vidéo à la Corbeille", Delete);
        var bar = NSStackView.FromViews([name, reveal, retake, delete]);
        bar.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;

        var content = window.ContentView!;
        foreach (var view in new NSView[] { bar, _playerView })
        {
            view.TranslatesAutoresizingMaskIntoConstraints = false;
            content.AddSubview(view);
        }
        NSLayoutConstraint.ActivateConstraints(
        [
            bar.TopAnchor.ConstraintEqualTo(content.TopAnchor, 8),
            bar.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 10),
            bar.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor, -10),
            _playerView.TopAnchor.ConstraintEqualTo(bar.BottomAnchor, 8),
            _playerView.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor),
            _playerView.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor),
            _playerView.BottomAnchor.ConstraintEqualTo(content.BottomAnchor)
        ]);

        window.WillClose += (_, _) =>
        {
            Release();
            Closed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>"Refaire": the caller sends the video to the Trash and records again the same way.</summary>
    public event EventHandler? RetakeRequested;

    public event EventHandler? Closed;

    private static NSWindow CreateWindow(string path)
    {
        var window = new NSWindow(new CGRect(0, 0, 960, 600),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered, false)
        {
            Title = "LocalTake — " + Path.GetFileName(path),
            ContentMinSize = new CGSize(640, 420),
            Appearance = NSAppearance.GetAppearance(NSAppearance.NameDarkAqua)
        };
        window.ReleaseWhenClosed(false);
        window.Center();
        return window;
    }

    public void Show()
    {
        NSApplication.SharedApplication.Activate();
        ShowWindow(this);
        Window!.MakeKeyAndOrderFront(this);
        Window.MakeFirstResponder(_playerView);
        _player?.Play();
    }

    /// <summary>From the area selection on, a preview still playing would end up in the new video's sound.</summary>
    public void PauseIfPlaying() => _player?.Pause();

    /// <summary>Escape closes the window, as on Windows.</summary>
    [Export("cancelOperation:")]
    public void CancelOperation(NSObject? sender) => Window?.PerformClose(sender);

    /// <summary>
    /// Sends the video to the Trash and closes the window. Returns false, with the video still shown, when it could
    /// not be moved.
    /// </summary>
    public bool Discard()
    {
        Release();
        try
        {
            if (File.Exists(_path))
                _trash.MoveToTrash(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MacDialogs.Show("La vidéo n'a pas pu être supprimée : " + ex.Message, NSAlertStyle.Warning);
            Load();
            return false;
        }
        Window?.Close();
        return true;
    }

    private void Delete()
    {
        _player?.Pause();
        if (MacDialogs.AskYesNo($"Envoyer « {Path.GetFileName(_path)} » à la Corbeille ?"))
            Discard();
    }

    private void Reveal()
    {
        if (File.Exists(_path))
            _shell.Reveal(_path);
        else
            MacDialogs.Show("La vidéo n'est plus à cet emplacement.", NSAlertStyle.Informational);
    }

    private void Load()
    {
        using var url = NSUrl.FromFilename(_path);
        _player = AVPlayer.FromUrl(url);
        _playerView.Player = _player;
    }

    /// <summary>The player lets go of the file before it is moved.</summary>
    private void Release()
    {
        _player?.Pause();
        _playerView.Player = null;
        _player?.Dispose();
        _player = null;
    }

    private static NSButton Button(string title, string symbol, string tip, Action action)
    {
        var button = NSButton.CreateButton(title, action);
        if (NSImage.GetSystemSymbol(symbol, null) is { } image)
        {
            button.Image = image;
            button.ImagePosition = NSCellImagePosition.ImageLeading;
        }
        button.ToolTip = tip;
        button.RefusesFirstResponder = true;
        return button;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Release();
            _playerView.Dispose();
        }
        base.Dispose(disposing);
    }
}
