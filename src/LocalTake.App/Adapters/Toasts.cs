using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LocalTake.Core.Ports;

namespace LocalTake.App.Adapters;

/// <summary>
/// Short notices in the corner of the main screen, above the notification area: Avalonia's tray icon has no
/// balloon. Clicking a "saved" notice reveals the file.
/// </summary>
internal sealed class Toasts(IShell shell) : INotifications
{
    private ToastWindow? _current;

    public void FileSaved(string path) =>
        Show("Capture enregistrée", path + "\nCliquez pour l'afficher dans le dossier.", () =>
        {
            if (File.Exists(path))
                shell.Reveal(path);
        });

    public void Info(string title, string text) => Show(title, text, null);

    private void Show(string title, string text, Action? onClick) => Dispatcher.UIThread.Post(() =>
    {
        _current?.Close();
        _current = new ToastWindow(title, text, onClick);
        _current.Closed += (sender, _) =>
        {
            if (_current == sender)
                _current = null;
        };
        _current.Show();
    });
}

internal sealed class ToastWindow : Window
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);
    private const int ScreenMargin = 12;

    public ToastWindow(string title, string text, Action? onClick)
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.Parse("#2B2B2B"));
        Cursor = onClick is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#1E88E5")),
            BorderThickness = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(16, 12),
            MaxWidth = 420,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gainsboro }
                }
            }
        };

        PointerPressed += (_, _) =>
        {
            onClick?.Invoke();
            Close();
        };
        Opened += (_, _) => PlaceInCorner();
        DispatcherTimer.RunOnce(Close, Lifetime);
    }

    private void PlaceInCorner()
    {
        if (Screens.Primary is not { } screen)
            return;
        var area = screen.WorkingArea;
        var scaling = screen.Scaling;
        var width = (int)Math.Ceiling(Bounds.Width * scaling);
        var height = (int)Math.Ceiling(Bounds.Height * scaling);
        var margin = (int)(ScreenMargin * scaling);
        Position = new PixelPoint(area.Right - width - margin, area.Bottom - height - margin);
    }
}
