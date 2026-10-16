using System.Globalization;
using LocalTake.Core.Geometry;
using LocalTake.Core.Recording;

namespace LocalTake.Core.Tests.Recording;

public sealed class RecordingCommandTests
{
    public sealed record ArgumentsCase(
        string Name, int X, int Y, int Width, int Height, int FrameRate, string? Microphone, bool SystemAudio,
        string Culture, string[] Arguments);

    public static TheoryData<string> LegacyCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var c in Fixtures.Load<List<ArgumentsCase>>("ffmpeg-arguments.json"))
            data.Add(c.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(LegacyCaseNames))]
    public void Windows_command_is_the_one_of_v0_1_0(string name)
    {
        var c = Fixtures.Load<List<ArgumentsCase>>("ffmpeg-arguments.json").Single(x => x.Name == name);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(c.Culture);
            var inputs = new DirectShowGdigrab().BuildInputs(
                new PixelRect(c.X, c.Y, c.Width, c.Height), new VideoOptions(c.FrameRate, c.Microphone, c.SystemAudio));

            RecordingCommand.Build(inputs, @"C:\Out\video.mp4").ShouldBe(c.Arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_video_filter_is_applied_to_the_screen_stream()
    {
        var inputs = new CaptureInputs([], ["-f", "lavfi", "-i", "testsrc"], VideoFilter: "crop=10:10:0:0");
        var args = RecordingCommand.Build(inputs, "out.mp4");
        args.ShouldContain("-vf");
        args[args.ToList().IndexOf("-vf") + 1].ShouldBe("crop=10:10:0:0");
    }
}
