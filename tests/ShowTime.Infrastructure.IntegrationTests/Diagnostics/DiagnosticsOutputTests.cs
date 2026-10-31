using ShowTime.Infrastructure.Diagnostics;

namespace ShowTime.Infrastructure.IntegrationTests.Diagnostics;

public sealed class DiagnosticsOutputTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShowTimeTests", Guid.NewGuid().ToString("N"));

    private string ReportFile => Path.Combine(_folder, "logs", "diagnostics.txt");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Report_goes_to_the_console_and_the_file()
    {
        using var console = new StringWriter();

        using (var output = DiagnosticsOutput.Open(ReportFile, console))
            output.WriteLine("[ok]   ffmpeg: /opt/homebrew/bin/ffmpeg");

        console.ToString().ShouldContain("[ok]   ffmpeg: /opt/homebrew/bin/ffmpeg");
        File.ReadAllText(ReportFile).ShouldContain("[ok]   ffmpeg: /opt/homebrew/bin/ffmpeg");
    }

    [Fact]
    public void Without_a_console_the_report_still_reaches_the_file()
    {
        using (var output = DiagnosticsOutput.Open(ReportFile, console: null))
            output.WriteLine("[ok]   x");

        File.ReadAllText(ReportFile).ShouldContain("[ok]   x");
    }

    [Fact]
    public void Report_replaces_the_previous_one()
    {
        using (var output = DiagnosticsOutput.Open(ReportFile, console: null))
            output.WriteLine("old");
        using (var output = DiagnosticsOutput.Open(ReportFile, console: null))
            output.WriteLine("new");

        File.ReadAllText(ReportFile).ShouldNotContain("old");
    }
}
