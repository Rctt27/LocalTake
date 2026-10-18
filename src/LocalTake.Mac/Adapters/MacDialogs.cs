using AppKit;
using Foundation;
using LocalTake.Core.Ports;
using UniformTypeIdentifiers;

namespace LocalTake.Mac.Adapters;

/// <summary>Modal alerts. LocalTake lives in the menu bar: it comes to the front first, or the alert hides behind.</summary>
internal sealed class MacDialogs : IUserDialogs
{
    private const string Title = "LocalTake";

    public void ShowError(string message) => Show(message, NSAlertStyle.Critical);

    public void ShowWarning(string message) => Show(message, NSAlertStyle.Warning);

    public void ShowInfo(string message) => Show(message, NSAlertStyle.Informational);

    public Task<DialogAnswer> AskYesNoCancelAsync(string message) => Task.FromResult(AskYesNoCancel(message));

    /// <summary>Oui, Non, Annuler; Escape answers Annuler.</summary>
    public static DialogAnswer AskYesNoCancel(string message)
    {
        using var alert = Create(message, NSAlertStyle.Warning);
        alert.AddButton("Oui");
        alert.AddButton("Non");
        alert.AddButton("Annuler").KeyEquivalent = "\u001B";
        return (long)alert.RunModal() switch
        {
            (long)NSAlertButtonReturn.First => DialogAnswer.Yes,
            (long)NSAlertButtonReturn.Second => DialogAnswer.No,
            _ => DialogAnswer.Cancel
        };
    }

    /// <summary>Oui, Non; Escape answers Non.</summary>
    public static bool AskYesNo(string message)
    {
        using var alert = Create(message, NSAlertStyle.Warning);
        alert.AddButton("Oui");
        alert.AddButton("Non").KeyEquivalent = "\u001B";
        return (long)alert.RunModal() == (long)NSAlertButtonReturn.First;
    }

    public static void Show(string message, NSAlertStyle style)
    {
        using var alert = Create(message, style);
        alert.RunModal();
    }

    private static NSAlert Create(string message, NSAlertStyle style)
    {
        AppActivation.BringToFront();
        return new NSAlert { AlertStyle = style, MessageText = Title, InformativeText = message };
    }

    /// <summary>"Ouvrir une image…": a PNG, JPEG, TIFF, HEIC or any image macOS reads.</summary>
    public static string? AskImageToOpen(string initialDirectory)
    {
        AppActivation.BringToFront();
        using var panel = NSOpenPanel.OpenPanel;
        panel.AllowedContentTypes = [UTTypes.Image];
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        if (Directory.Exists(initialDirectory))
            panel.DirectoryUrl = NSUrl.FromFilename(initialDirectory);
        return panel.RunModal() == 1 ? panel.Url?.Path : null;
    }
}

/// <summary>The "Enregistrer sous…" panel, PNG or JPEG after the extension typed.</summary>
internal sealed class MacSaveFileDialog : ISaveFileDialog
{
    public Task<string?> AskAsync(string suggestedName, bool jpeg, string initialDirectory)
    {
        using var panel = NSSavePanel.SavePanel;
        panel.AllowedContentTypes = jpeg ? [UTTypes.Jpeg, UTTypes.Png] : [UTTypes.Png, UTTypes.Jpeg];
        panel.NameFieldStringValue = suggestedName + (jpeg ? ".jpg" : ".png");
        panel.CanCreateDirectories = true;
        if (Directory.Exists(initialDirectory))
            panel.DirectoryUrl = NSUrl.FromFilename(initialDirectory);
        return Task.FromResult(panel.RunModal() == 1 ? panel.Url?.Path : null);
    }
}
