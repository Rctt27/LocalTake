using ShowTime.Core.Files;
using ShowTime.Core.Ports;
using ShowTime.Core.Settings;

namespace ShowTime.Core.Capture;

/// <summary>What happens to a capture: the editor, a file, the clipboard, or both.</summary>
public sealed class CaptureDelivery(
    AppSettings settings, ImageSaver saver, IClipboard clipboard, IEditorLauncher editor, INotifications notifications)
{
    /// <summary>The editor takes the image over; otherwise it is released once saved or copied.</summary>
    public void Deliver(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (settings.AfterCapture == AfterCaptureAction.OpenEditor)
        {
            editor.Open(image);
            return;
        }

        using (image)
        {
            switch (settings.AfterCapture)
            {
                case AfterCaptureAction.SaveToFile:
                    notifications.FileSaved(saver.SaveToOutputFolder(image));
                    break;
                case AfterCaptureAction.CopyToClipboard:
                    clipboard.SetImage(image);
                    notifications.Info("Capture copiée", "L'image est dans le presse-papier.");
                    break;
                case AfterCaptureAction.SaveAndCopy:
                    var path = saver.SaveToOutputFolder(image);
                    // Copying the path, if enabled, must not replace the image in the clipboard.
                    clipboard.SetImage(image);
                    notifications.FileSaved(path);
                    break;
            }
        }
    }
}
