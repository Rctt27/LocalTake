namespace ShowTime.Windows.Tests;

/// <summary>
/// Tests that record the top left of the screen with ffmpeg: run one at a time, or one's windows would appear in the
/// other's video.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScreenRecordings
{
    public const string Name = "Screen recordings";
}
