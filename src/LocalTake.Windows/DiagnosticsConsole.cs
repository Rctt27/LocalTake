using System;
using System.IO;
using System.Text;
using LocalTake.Platform.Windows;

namespace LocalTake;

/// <summary>Where "LocalTake.exe --diagnostics" writes its report: the terminal it was started from, and a file.</summary>
internal static class DiagnosticsConsole
{
    /// <param name="reportFile">Always written, since a shortcut or a double-click has no terminal to show the report.</param>
    public static TextWriter Open(string reportFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
        var file = new StreamWriter(reportFile, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (!WindowsConsole.AttachToParent() && !Console.IsOutputRedirected)
            return file;
        var console = new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true };
        return new TeeWriter(console, file);
    }

    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => second.Encoding;

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                first.Dispose();
                second.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
