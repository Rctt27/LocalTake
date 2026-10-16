using System.Text.Json;

namespace LocalTake.Core.Tests;

/// <summary>Reads the behaviour recorded from v0.1.0 (tests/fixtures/legacy).</summary>
internal static class Fixtures
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "legacy", name);

    public static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path(name)), Options)
        ?? throw new InvalidOperationException("Empty fixture " + name);
}
