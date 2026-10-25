namespace LocalTake.Core.Recording;

/// <summary>
/// The jumps of the video player (← and →, and its buttons on Windows). Five seconds: the videos rarely last more than
/// two or three minutes.
/// </summary>
public static class PlayerSeek
{
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(5);

    public static TimeSpan Back(TimeSpan position) => position > Step ? position - Step : TimeSpan.Zero;

    /// <param name="duration">The video's length, or zero while the player does not know it yet.</param>
    public static TimeSpan Forward(TimeSpan position, TimeSpan duration)
    {
        var target = position + Step;
        return duration > TimeSpan.Zero && target > duration ? duration : target;
    }
}
