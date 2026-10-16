using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LocalTake.App.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalTake.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = Composition.Build();
            var log = _services.GetRequiredService<ILogger<App>>();
            if (log.IsEnabled(LogLevel.Information))
                log.LogInformation("LocalTake {Version} started on {OS}", typeof(App).Assembly.GetName().Version, Environment.OSVersion);

            // One failed action must not take down the tray process and every open editor with it.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                log.LogError(e.Exception, "Unhandled exception");
                Adapters.MessageWindow.ShowAsync("Une erreur inattendue s'est produite : " + e.Exception.Message, "#E53935",
                    ("OK", Core.Ports.DialogAnswer.Yes));
                e.Handled = true;
            };

            _services.GetRequiredService<TrayController>();
            desktop.Exit += (_, _) => _services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
