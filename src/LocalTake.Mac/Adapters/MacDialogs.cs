using AppKit;
using LocalTake.Core.Ports;

namespace LocalTake.Mac.Adapters;

/// <summary>Modal alerts. LocalTake lives in the menu bar: it comes to the front first, or the alert hides behind.</summary>
internal sealed class MacDialogs : IUserDialogs
{
    private const string Title = "LocalTake";

    public void ShowError(string message) => Show(message, NSAlertStyle.Critical);

    public void ShowWarning(string message) => Show(message, NSAlertStyle.Warning);

    public void ShowInfo(string message) => Show(message, NSAlertStyle.Informational);

    public Task<DialogAnswer> AskYesNoCancelAsync(string message)
    {
        using var alert = Create(message, NSAlertStyle.Warning);
        alert.AddButton("Oui");
        alert.AddButton("Non");
        alert.AddButton("Annuler").KeyEquivalent = "\u001B";
        return Task.FromResult((long)alert.RunModal() switch
        {
            (long)NSAlertButtonReturn.First => DialogAnswer.Yes,
            (long)NSAlertButtonReturn.Second => DialogAnswer.No,
            _ => DialogAnswer.Cancel
        });
    }

    private static void Show(string message, NSAlertStyle style)
    {
        using var alert = Create(message, style);
        alert.RunModal();
    }

    private static NSAlert Create(string message, NSAlertStyle style)
    {
        NSApplication.SharedApplication.Activate();
        return new NSAlert { AlertStyle = style, MessageText = Title, InformativeText = message };
    }
}
