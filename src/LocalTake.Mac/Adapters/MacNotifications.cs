using Foundation;
using LocalTake.Core.Ports;
using Microsoft.Extensions.Logging;
using UserNotifications;

namespace LocalTake.Mac.Adapters;

/// <summary>
/// Local notifications from the Notification Centre; nothing leaves the Mac. macOS asks the user once whether
/// LocalTake may show them; when refused, a notice is only written to the log.
/// </summary>
internal sealed class MacNotifications : UNUserNotificationCenterDelegate, INotifications
{
    private const string PathKey = "path";

    private readonly IShell _shell;
    private readonly IUiThread _uiThread;
    private readonly ILogger<MacNotifications> _log;

    public MacNotifications(IShell shell, IUiThread uiThread, ILogger<MacNotifications> log)
    {
        _shell = shell;
        _uiThread = uiThread;
        _log = log;
        UNUserNotificationCenter.Current.Delegate = this;
    }

    public void FileSaved(string path) =>
        Send("Capture enregistrée", Path.GetFileName(path) + " — cliquez pour l'afficher dans le Finder.", path);

    public void Info(string title, string text) => Send(title, text, path: null);

    /// <summary>Shown even while LocalTake is the active application, as it is while an editor is open.</summary>
    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);

    /// <summary>A click on a saved capture's notification shows the file in the Finder.</summary>
    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response,
        Action completionHandler)
    {
        if (response.Notification.Request.Content.UserInfo[PathKey] is NSString path)
        {
            var file = path.ToString();
            _uiThread.Post(() => _shell.Reveal(file));
        }
        completionHandler();
    }

    private void Send(string title, string text, string? path)
    {
        var center = UNUserNotificationCenter.Current;
        center.RequestAuthorization(UNAuthorizationOptions.Alert, (granted, error) =>
        {
            if (!granted)
            {
                _log.LogInformation("Notification not shown, not allowed by the user: {Title} ({Error})", title, error?.LocalizedDescription);
                return;
            }
            using var content = new UNMutableNotificationContent { Title = title, Body = text };
            if (path is not null)
                content.UserInfo = NSDictionary.FromObjectAndKey(new NSString(path), new NSString(PathKey));
            using var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString("N"), content, null);
            center.AddNotificationRequest(request, failure =>
            {
                if (failure is not null)
                    _log.LogWarning("Notification failed: {Error}", failure.LocalizedDescription);
            });
        });
    }
}
