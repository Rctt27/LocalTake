namespace ShowTime.Core.Recording;

public static class AudioDevices
{
    public static IEnumerable<string> Microphones(IEnumerable<string> devices, string systemAudioDevice) =>
        devices.Where(d => d != systemAudioDevice);

    /// <summary>
    /// The chosen microphone when it is plugged in, otherwise the first one found. With an empty list (ffmpeg could
    /// not list the devices), the chosen one is tried anyway.
    /// </summary>
    public static string? PickMicrophone(IReadOnlyCollection<string> devices, string preferred, string systemAudioDevice)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count == 0)
            return string.IsNullOrWhiteSpace(preferred) ? null : preferred;
        var microphones = Microphones(devices, systemAudioDevice).ToList();
        return microphones.Contains(preferred) ? preferred : microphones.FirstOrDefault();
    }
}
