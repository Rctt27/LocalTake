using System.Windows.Threading;

namespace ShowTime.Windows.Tests;

/// <summary>
/// Runs test code on one STA thread that hosts ShowTime's WPF application, as its windows need: they are STA-bound
/// and look up their styles in App.xaml. The application is created without starting it (no tray icon, no shortcuts).
/// </summary>
internal static class Sta
{
    private static readonly Lazy<Dispatcher> UiThread = new(Start);

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "WPF tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    public static Task Run(Action action) => UiThread.Value.InvokeAsync(action).Task;

    public static Task<T> Run<T>(Func<T> action) => UiThread.Value.InvokeAsync(action).Task;

    /// <summary>Lets the dispatcher run everything queued so far: layout, Loaded handlers, rendering.</summary>
    public static void DoEvents() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
