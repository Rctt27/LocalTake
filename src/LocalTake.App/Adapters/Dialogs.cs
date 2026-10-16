using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LocalTake.Core.Ports;

namespace LocalTake.App.Adapters;

/// <summary>Modal-looking messages: Avalonia has no system message box, and a tray app has no window to own one.</summary>
internal sealed class Dialogs : IUserDialogs
{
    public void ShowError(string message) => _ = MessageWindow.ShowAsync(message, "#E53935", ("OK", DialogAnswer.Yes));

    public void ShowWarning(string message) => _ = MessageWindow.ShowAsync(message, "#FB8C00", ("OK", DialogAnswer.Yes));

    public void ShowInfo(string message) => _ = MessageWindow.ShowAsync(message, "#1E88E5", ("OK", DialogAnswer.Yes));

    public Task<DialogAnswer> AskYesNoCancelAsync(string message) =>
        MessageWindow.ShowAsync(message, "#1E88E5", ("Oui", DialogAnswer.Yes), ("Non", DialogAnswer.No), ("Annuler", DialogAnswer.Cancel));
}

internal sealed class MessageWindow : Window
{
    private readonly TaskCompletionSource<DialogAnswer> _answer = new();

    private MessageWindow(string message, string accent, (string Label, DialogAnswer Answer)[] buttons)
    {
        Title = "LocalTake";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MaxWidth = 560;

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (label, answer) in buttons)
        {
            var button = new Button { Content = label, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => Close(answer);
            row.Children.Add(button);
        }
        ((Button)row.Children[0]).Classes.Add("accent");

        Content = new DockPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new Border { Width = 4, Background = Brush.Parse(accent), Margin = new Thickness(0, 0, 14, 0), [DockPanel.DockProperty] = Dock.Left },
                new StackPanel
                {
                    Spacing = 18,
                    Children =
                    {
                        new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
                        row
                    }
                }
            }
        };

        // Escape answers with the last button (Cancel, or OK); closing the window too.
        var fallback = buttons[^1].Answer;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(fallback);
        };
        Closed += (_, _) => _answer.TrySetResult(fallback);
    }

    public static Task<DialogAnswer> ShowAsync(string message, string accent, params (string Label, DialogAnswer Answer)[] buttons) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new MessageWindow(message, accent, buttons);
            window.Show();
            window.Activate();
            return window._answer.Task;
        });

    private void Close(DialogAnswer answer)
    {
        _answer.TrySetResult(answer);
        Close();
    }
}
