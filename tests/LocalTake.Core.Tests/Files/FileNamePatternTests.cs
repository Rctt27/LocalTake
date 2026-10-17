using LocalTake.Core.Files;

namespace LocalTake.Core.Tests.Files;

public sealed class FileNamePatternTests
{
    public sealed record ExpandCase(string Pattern, int Counter, string Result);

    private static readonly DateTime When = new(2026, 3, 7, 9, 5, 2);

    public static TheoryData<string, int, string> LegacyCases()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var c in Fixtures.Load<List<ExpandCase>>("expand-pattern.json"))
            data.Add(c.Pattern, c.Counter, c.Result);
        return data;
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public void Expand_produces_the_same_names_as_v0_1_0(string pattern, int counter, string expected) =>
        FileNamePattern.Expand(pattern, counter, When).ShouldBe(expected);

    [Fact]
    public void Expand_replaces_characters_that_Windows_and_macOS_reject_on_every_platform() =>
        // Path.GetInvalidFileNameChars() only lists '/' and NUL on Unix; names must not depend on the OS.
        FileNamePattern.Expand("a:b|c?d", 1, When).ShouldBe("a_b_c_d");

    [Fact]
    public void Expand_uses_the_Gregorian_year_whatever_the_current_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            FileNamePattern.Expand("%YYYY%", 1, When).ShouldBe("2026");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Capture_%NUM%", true)]
    [InlineData("Capture_%YYYY%", false)]
    [InlineData("%num%", false)]
    public void UsesCounter_detects_the_NUM_token(string pattern, bool expected) =>
        FileNamePattern.UsesCounter(pattern).ShouldBe(expected);

    [Fact]
    public void Default_pattern_is_the_one_of_v0_1_0() =>
        FileNamePattern.Default.ShouldBe("Capture_%YYYY%-%MM%-%DD%_%hh%-%mm%-%ss%");
}
