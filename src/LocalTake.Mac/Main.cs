using AppKit;
using Foundation;
using LocalTake.Core.Ports;
using LocalTake.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LocalTake.Mac;

internal static class Program
{
    private static void Main(string[] args)
    {
        NSApplication.Init();
        var app = NSApplication.SharedApplication;

        // "LocalTake.app/Contents/MacOS/LocalTake --diagnostics", from a terminal. It runs inside the application's run
        // loop, as the app does, since the shortcut check needs the main thread.
        if (args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
        {
            app.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
            app.BeginInvokeOnMainThread(async () => Environment.Exit(await RunDiagnosticsAsync().ConfigureAwait(true)));
            app.Run();
            return;
        }

        // A second launch, from the Finder or at login, leaves the running one alone instead of adding an icon.
        // Compared by process, since an app started from a terminal is not listed until it has finished launching.
        var bundleId = NSBundle.MainBundle.BundleIdentifier;
        if (bundleId is not null && NSRunningApplication.GetRunningApplications(bundleId)
                .Any(other => other.ProcessIdentifier != Environment.ProcessId))
            return;

        app.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        app.MainMenu = MainMenu.Create();
        app.Delegate = new AppDelegate();
        app.Run();
    }

    private static async Task<int> RunDiagnosticsAsync()
    {
        try
        {
            using var services = Composition.Build();
            var reportFile = Path.Combine(services.GetRequiredService<IAppPaths>().LogsFolder, "diagnostics.txt");
            using var output = DiagnosticsOutput.Open(reportFile, Console.Out);
            var code = await services.GetRequiredService<DiagnosticsReport>().RunAsync(output).ConfigureAwait(true);
            output.WriteLine("Report saved to " + reportFile);
            return code;
        }
#pragma warning disable CA1031 // Whatever stops the report is the most useful thing it can print.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync("Diagnostics failed: " + ex).ConfigureAwait(true);
            return 1;
        }
    }
}
