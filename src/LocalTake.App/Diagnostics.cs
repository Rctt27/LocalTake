using LocalTake.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LocalTake.App;

/// <summary>"LocalTake --diagnostics": the report of <see cref="DiagnosticsReport"/>, on the standard output.</summary>
internal static class Diagnostics
{
    public static async Task<int> RunAsync(TextWriter output)
    {
        using var services = Composition.Build();
        return await services.GetRequiredService<DiagnosticsReport>().RunAsync(output);
    }
}
