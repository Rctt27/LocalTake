using System.Globalization;
using System.Text.RegularExpressions;

namespace ShowTime.Core.Transcription;

/// <summary>A time as the subtitle editor shows it and as the user types it.</summary>
public static partial class Timecode
{
    /// <summary>"01:02.345", with the hours only when there are some: "1:02:05.042".</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        var culture = CultureInfo.InvariantCulture;
        return time.TotalHours >= 1
            ? string.Create(culture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}")
            : string.Create(culture, $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    /// <summary>Seconds, minutes:seconds or hours:minutes:seconds, with an optional fraction after a dot or a comma.</summary>
    public static bool TryParse(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var match = Pattern().Match(text ?? "");
        if (!match.Success)
            return false;

        var parts = match.Groups["parts"].Value.Split(':');
        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        // Below the leading part, minutes and seconds count up to 59.
        if (numbers.Skip(1).Any(n => n > 59))
            return false;
        var seconds = numbers.Aggregate(0L, (total, n) => total * 60 + n);
        var fraction = match.Groups["fraction"].Value;
        var milliseconds = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        value = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    [GeneratedRegex(@"^\s*(?<parts>\d+(:\d{1,2}){0,2})([.,](?<fraction>\d{1,3}))?\s*$")]
    private static partial Regex Pattern();
}
