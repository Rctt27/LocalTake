using System.IO;

namespace ShowTime.Windows.Tests;

public sealed class DiagnosticsConsoleTests
{
    [Fact]
    public void Report_is_written_to_the_file_without_a_console()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ShowTimeTests", Guid.NewGuid().ToString("N"));
        var report = Path.Combine(folder, "diagnostics.txt");
        try
        {
            using (var output = DiagnosticsConsole.Open(report))
                output.WriteLine("[ok]   x");

            File.ReadAllText(report).ShouldContain("[ok]   x");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
