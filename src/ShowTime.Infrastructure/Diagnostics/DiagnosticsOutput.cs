using System.Text;

namespace ShowTime.Infrastructure.Diagnostics;

/// <summary>Where "--diagnostics" writes its report: a file, and the terminal ShowTime was started from, if any.</summary>
public static class DiagnosticsOutput
{
    /// <param name="reportFile">Always written, since an app started from a shortcut or the Finder has no terminal.</param>
    /// <param name="console">The terminal, or null when there is none.</param>
    public static TextWriter Open(string reportFile, TextWriter? console)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile)!);
        var file = new StreamWriter(reportFile, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return console is null ? file : new TeeWriter(console, file);
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
