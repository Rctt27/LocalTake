using LocalTake.Core.Recording;
using Microsoft.Extensions.DependencyInjection;

namespace LocalTake.Windows.Tests;

/// <summary>ValidateOnBuild does not look inside factories: the recording session is resolved for real.</summary>
public sealed class CompositionTests
{
    [Fact]
    public async Task The_recording_session_and_its_highlighter_can_be_built() => await Sta.Run(() =>
    {
        using var services = App.ConfigureServices(System.Windows.Threading.Dispatcher.CurrentDispatcher).BuildServiceProvider();
        services.GetRequiredService<RecordingSession>().ShouldNotBeNull();
        services.GetRequiredService<Highlighter>().ShouldNotBeNull();
    });
}
