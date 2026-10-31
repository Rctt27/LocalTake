using ShowTime.Core.Transcription;

namespace ShowTime.Core.Tests.Transcription;

public sealed class SubtitleProfileTests
{
    private static SubtitleCue Cue(double seconds, string text) => new(TimeSpan.Zero, TimeSpan.FromSeconds(seconds), text);

    [Fact]
    public void Reading_speed_counts_the_characters_shown_per_second_without_line_breaks()
    {
        SubtitleProfile.ReadingSpeed(Cue(2, "Bonjour\nà tous")).ShouldBe(6.5);
    }

    [Fact]
    public void Reading_speed_of_a_cue_without_duration_is_zero() => SubtitleProfile.ReadingSpeed(Cue(0, "x")).ShouldBe(0);

    [Fact]
    public void A_cue_within_the_profile_has_nothing_to_report()
    {
        SubtitleProfile.Tutorial.Check(Cue(3, "On choisit la zone de l'écran.")).ShouldBeEmpty();
    }

    [Fact]
    public void A_fast_cue_is_reported_against_the_comfortable_then_the_maximum_speed()
    {
        // 32 characters in 2 s: 16 per second, above 15; in 1.8 s: about 18, above 17.
        var tutorial = SubtitleProfile.Tutorial;

        tutorial.Check(Cue(2, "On choisit la zone de l'écran ok")).ShouldBe(
            [new SubtitleWarning(SubtitleWarningLevel.Notice, "16 caractères par seconde : un peu rapide à lire (15 au plus, idéalement).")]);
        tutorial.Check(Cue(1.8, "On choisit la zone de l'écran ok")).ShouldBe(
            [new SubtitleWarning(SubtitleWarningLevel.Problem, "18 caractères par seconde : trop rapide à lire (17 au plus).")]);
    }

    [Fact]
    public void Lines_too_long_or_too_many_are_reported()
    {
        var warnings = SubtitleProfile.Tutorial.Check(Cue(20, new string('a', 43) + "\nb\nc"));

        warnings.ShouldBe(
        [
            new SubtitleWarning(SubtitleWarningLevel.Problem, "Une ligne dépasse 42 caractères."),
            new SubtitleWarning(SubtitleWarningLevel.Problem, "Plus de 2 lignes.")
        ]);
    }
}
