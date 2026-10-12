using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>Countdown, elapsed time, Stop and Cancel. Excluded from capture like <see cref="RecordingFrame"/>.</summary>
public partial class RecordingBar : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stopwatch _elapsed = new();

    public event Action? StopRequested;
    public event Action? CancelRequested;

    public RecordingBar()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
            Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
        _timer.Tick += (_, _) =>
        {
            StatusText.Text = $"Enregistrement  {_elapsed.Elapsed:mm\\:ss}";
            Dot.Opacity = Dot.Opacity < 1 ? 1 : 0.35;
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Below the recorded area when there is room, otherwise above it, otherwise inside it.</summary>
    /// <param name="area">Physical screen pixels.</param>
    public void ShowNear(Drawing.Rectangle area)
    {
        Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(hwnd, out var r);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        var screen = Forms.Screen.FromRectangle(area).WorkingArea;
        const int gap = 10;

        var x = Math.Clamp(area.X + (area.Width - width) / 2, screen.Left, Math.Max(screen.Left, screen.Right - width));
        int y;
        if (area.Bottom + gap + height <= screen.Bottom)
            y = area.Bottom + gap;
        else if (area.Top - gap - height >= screen.Top)
            y = area.Top - gap - height;
        else
            y = screen.Bottom - height - gap;

        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    public void ShowCountdown(int seconds) => StatusText.Text = $"Début dans {seconds}…";

    public void ShowRecording()
    {
        _elapsed.Start();
        StatusText.Text = "Enregistrement  00:00";
        _timer.Start();
    }

    public void ShowFinishing()
    {
        _timer.Stop();
        Dot.Opacity = 0.35;
        StatusText.Text = "Finalisation…";
        StopButton.IsEnabled = CancelButton.IsEnabled = false;
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
