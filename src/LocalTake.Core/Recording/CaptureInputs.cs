namespace LocalTake.Core.Recording;

/// <summary>The ffmpeg inputs of a recording, as one platform captures the screen and the sound.</summary>
/// <param name="AudioInputs">The arguments of each audio input, each ending with its "-i".</param>
/// <param name="VideoInput">The arguments of the screen input, ending with its "-i".</param>
/// <param name="VideoFilter">A filter for the screen stream, such as a crop when the input captures a whole display.</param>
public sealed record CaptureInputs(
    IReadOnlyList<IReadOnlyList<string>> AudioInputs, IReadOnlyList<string> VideoInput, string? VideoFilter = null);
