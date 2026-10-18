using System;
using System.IO;
using LocalTake.Infrastructure.Diagnostics;
using LocalTake.Platform.Windows;

namespace LocalTake;

/// <summary>Where "LocalTake.exe --diagnostics" writes its report: the terminal it was started from, and a file.</summary>
internal static class DiagnosticsConsole
{
    /// <param name="reportFile">Always written, since a shortcut or a double-click has no terminal to show the report.</param>
    public static TextWriter Open(string reportFile)
    {
        // A WinExe has no console of its own: it borrows the one of the terminal that started it, if any.
        var console = WindowsConsole.AttachToParent() || Console.IsOutputRedirected
            ? new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true }
            : null;
        return DiagnosticsOutput.Open(reportFile, console);
    }
}
