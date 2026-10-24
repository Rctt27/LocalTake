namespace LocalTake.Core.Recording;

/// <param name="FrameRate">Frames per second.</param>
/// <param name="Microphone">Audio input to record, or null for none.</param>
/// <param name="SystemAudio">Whether to record what the computer plays.</param>
/// <param name="MicrophoneTrackPath">
/// A WAV file to write the microphone to, alone, for the transcription; null for none. Only with a microphone.
/// </param>
public sealed record VideoOptions(int FrameRate, string? Microphone, bool SystemAudio, string? MicrophoneTrackPath = null);
