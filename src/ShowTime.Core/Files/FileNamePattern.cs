using System.Globalization;

namespace ShowTime.Core.Files;

/// <summary>File names built from a pattern: %YYYY% %MM% %DD% %hh% %mm% %ss% %NUM%.</summary>
public static class FileNamePattern
{
    public const string Default = "Capture_%YYYY%-%MM%-%DD%_%hh%-%mm%-%ss%";

    private const string CounterToken = "%NUM%";

    /// <summary>
    /// Characters Windows rejects in a file name, which also covers macOS (':' and '/'). Fixed rather than taken from
    /// <see cref="Path.GetInvalidFileNameChars"/>, which differs by platform, so that a pattern names files alike
    /// everywhere.
    /// </summary>
    private static readonly char[] InvalidCharacters =
        [.. Enumerable.Range(0, 32).Select(c => (char)c), '"', '<', '>', '|', ':', '*', '?', '\\', '/'];

    /// <summary>
    /// File name for a pattern, without extension. Side-effect free: the settings preview uses it too, and %NUM% only
    /// advances once a file is actually written (see <see cref="FileCounter"/>).
    /// </summary>
    public static string Expand(string pattern, int counter, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var culture = CultureInfo.InvariantCulture;
        var name = pattern
            .Replace("%YYYY%", now.ToString("yyyy", culture), StringComparison.Ordinal)
            .Replace("%MM%", now.ToString("MM", culture), StringComparison.Ordinal)
            .Replace("%DD%", now.ToString("dd", culture), StringComparison.Ordinal)
            .Replace("%hh%", now.ToString("HH", culture), StringComparison.Ordinal)
            .Replace("%mm%", now.ToString("mm", culture), StringComparison.Ordinal)
            .Replace("%ss%", now.ToString("ss", culture), StringComparison.Ordinal)
            .Replace(CounterToken, counter.ToString("D6", culture), StringComparison.Ordinal);

        foreach (var c in InvalidCharacters)
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Capture" : name;
    }

    public static bool UsesCounter(string pattern) => pattern.Contains(CounterToken, StringComparison.Ordinal);
}
