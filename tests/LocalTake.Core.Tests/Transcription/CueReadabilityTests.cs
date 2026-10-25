using LocalTake.Core.Transcription;

namespace LocalTake.Core.Tests.Transcription;

public sealed class CueReadabilityTests
{
    private static SubtitleCue Cue(double seconds, string text) => new(TimeSpan.Zero, TimeSpan.FromSeconds(seconds), text);

    [Fact]
    public void An_easy_cue_shows_its_speed_and_why_it_is_fine() =>
        CueReadability.Of(Cue(2, "Bonjour")).ShouldBe(new CueReadability("4 car./s", ReadabilityLevel.Easy,
            "Caractères affichés par seconde : facile à lire (15 au plus, idéalement)."));

    [Fact]
    public void A_slightly_fast_cue_is_a_notice_with_its_warning()
    {
        // 16 characters in one second.
        var readability = CueReadability.Of(Cue(1, "abcdefghijklmnop"));

        readability.Level.ShouldBe(ReadabilityLevel.Notice);
        readability.Speed.ShouldBe("16 car./s");
        readability.ToolTip.ShouldBe("16 caractères par seconde : un peu rapide à lire (15 au plus, idéalement).");
    }

    [Fact]
    public void A_cue_too_fast_or_too_long_is_a_problem_with_every_warning()
    {
        var readability = CueReadability.Of(Cue(1, new string('a', 43)));

        readability.Level.ShouldBe(ReadabilityLevel.Problem);
        readability.ToolTip.ShouldBe("Une ligne dépasse 42 caractères." + Environment.NewLine +
                                     "43 caractères par seconde : trop rapide à lire (17 au plus).");
    }
}
