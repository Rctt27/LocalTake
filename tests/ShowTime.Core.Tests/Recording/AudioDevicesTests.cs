using ShowTime.Core.Recording;

namespace ShowTime.Core.Tests.Recording;

public sealed class AudioDevicesTests
{
    public sealed record PickCase(string[] Devices, string Preferred, string? Result);

    public static TheoryData<string[], string, string?> LegacyCases()
    {
        var data = new TheoryData<string[], string, string?>();
        foreach (var c in Fixtures.Load<List<PickCase>>("pick-microphone.json"))
            data.Add(c.Devices, c.Preferred, c.Result);
        return data;
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void PickMicrophone_chooses_as_v0_1_0(string[] devices, string preferred, string? expected) =>
        AudioDevices.PickMicrophone(devices, preferred, "virtual-audio-capturer").ShouldBe(expected);

    [Fact]
    public void Microphones_leave_out_the_system_audio_device() =>
        AudioDevices.Microphones(["Mic", "BlackHole 2ch"], "BlackHole 2ch").ShouldBe(["Mic"]);
}
