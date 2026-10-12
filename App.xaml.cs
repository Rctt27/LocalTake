using System.Threading;
using System.Windows;

namespace LocalTake;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\LocalTake.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("LocalTake est déjà lancé (icône dans la zone de notification).", "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // One failed action must not take down the tray process and every open editor with it.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("Une erreur inattendue s'est produite : " + args.Exception.Message, "LocalTake",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        System.Windows.Forms.Application.EnableVisualStyles();
        _tray = new TrayController(AppSettings.Load());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
