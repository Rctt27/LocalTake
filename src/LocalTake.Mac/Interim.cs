using LocalTake.Core.Files;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;

namespace LocalTake.Mac;

// Stand-ins for what the next steps of the macOS app bring: the editor, then the recording.

/// <summary>Until the editor exists, "open in the editor" saves the capture.</summary>
internal sealed class SaveInsteadOfEditing(ImageSaver saver, INotifications notifications) : IEditorLauncher
{
    public void Open(IImage image)
    {
        using (image)
            notifications.FileSaved(saver.SaveToOutputFolder(image));
    }
}

/// <summary>Until recording exists, nothing is ever being recorded.</summary>
internal sealed class NoRecording : IRecordingStatus
{
    public bool IsBusy => false;
}
