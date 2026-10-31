using System.Diagnostics;
using Microsoft.Win32;
using ShowTime.Core.Ports;

namespace ShowTime.Platform.Windows;

/// <summary>Explorer.</summary>
public sealed class WindowsShell : IShell
{
    public void Reveal(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();

    public void OpenFolder(string path) => Process.Start("explorer.exe", $"\"{path}\"")?.Dispose();

    public void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}

/// <summary>The Recycle Bin, through the shell so that the user can restore the file.</summary>
public sealed unsafe class WindowsFileTrash : IFileTrash
{
    public void MoveToTrash(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        // pFrom is a list of paths ended by an extra null character.
        var from = Path.GetFullPath(path) + "\0\0";
        fixed (char* paths = from)
        {
            var operation = new Win32.SHFILEOPSTRUCTW
            {
                wFunc = Win32.FO_DELETE,
                pFrom = paths,
                fFlags = Win32.FOF_ALLOWUNDO | Win32.FOF_NOCONFIRMATION | Win32.FOF_SILENT | Win32.FOF_NOERRORUI
            };
            var result = Win32.SHFileOperationW(&operation);
            if (result != 0 || operation.fAnyOperationsAborted != 0)
                throw new IOException($"Le fichier n'a pas pu être envoyé à la Corbeille (code {result}).");
        }
    }
}

/// <summary>The Run key of the current user.</summary>
/// <param name="runKeyPath">Under HKEY_CURRENT_USER.</param>
/// <param name="valueName">The value naming ShowTime.</param>
/// <param name="executablePath">The program to start.</param>
public sealed class WindowsStartupRegistration(string runKeyPath, string valueName, string executablePath) : IStartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
            return key?.GetValue(valueName) is not null;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (enabled)
            key.SetValue(valueName, $"\"{executablePath}\"");
        else if (key.GetValue(valueName) is not null)
            key.DeleteValue(valueName);
    }
}
