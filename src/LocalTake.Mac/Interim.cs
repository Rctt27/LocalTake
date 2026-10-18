using LocalTake.Core.Recording;

namespace LocalTake.Mac;

// A stand-in for what the next step of the macOS app brings: the recording.

/// <summary>Until recording exists, nothing is ever being recorded.</summary>
internal sealed class NoRecording : IRecordingStatus
{
    public bool IsBusy => false;
}
