namespace ShowTime.Core.Ports;

public enum DialogAnswer
{
    Yes,
    No,
    Cancel
}

/// <summary>Modal messages.</summary>
public interface IUserDialogs
{
    void ShowError(string message);
    void ShowWarning(string message);
    void ShowInfo(string message);
    Task<DialogAnswer> AskYesNoCancelAsync(string message);
}

/// <summary>Short notices that do not interrupt, such as the tray balloon.</summary>
public interface INotifications
{
    /// <summary>A file was saved; clicking the notice reveals it.</summary>
    void FileSaved(string path);

    /// <summary>A notice unrelated to a file.</summary>
    void Info(string title, string text);
}

/// <summary>Opens the annotation editor on an image.</summary>
public interface IEditorLauncher
{
    void Open(IImage image);
}

/// <summary>Runs an action on the UI thread, from any thread.</summary>
public interface IUiThread
{
    void Post(Action action);
}

/// <summary>The system clipboard. Implementations retry while another process briefly holds it.</summary>
public interface IClipboard
{
    void SetImage(IImage image);
    void SetText(string text);
}

/// <summary>Writes an image file, PNG or JPEG after its extension.</summary>
public interface IImageCodec
{
    void Save(IImage image, string path, int jpegQuality);
}

/// <summary>The "Save as" dialog.</summary>
public interface ISaveFileDialog
{
    /// <param name="suggestedName">File name without extension.</param>
    /// <param name="jpeg">Whether JPEG is the format offered first.</param>
    /// <param name="initialDirectory">The folder the dialog opens in.</param>
    /// <returns>The chosen path, or null when cancelled.</returns>
    Task<string?> AskAsync(string suggestedName, bool jpeg, string initialDirectory);
}
