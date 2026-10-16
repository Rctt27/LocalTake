using Avalonia;

namespace LocalTake.App;

internal static class Program
{
    /// <summary>One LocalTake per user session: a second launch would fight over the global shortcuts.</summary>
    private const string SingleInstanceName = "LocalTake.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--diagnostics"))
            return Diagnostics.RunAsync(Console.Out).GetAwaiter().GetResult();

        using var singleInstance = new Mutex(true, SingleInstanceName, out var isFirst);
        if (!isFirst)
        {
            // The running instance is in the notification area (Windows) or the menu bar (macOS).
            return 1;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Also used by the visual designer and the headless tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
