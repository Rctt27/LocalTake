using LocalTake.Core.Transcription;

namespace LocalTake.Core.Tests.Transcription;

public sealed class SubtitleDocumentTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
    private static SubtitleCue Cue(double start, double end, string text = "x") => new(S(start), S(end), text);

    /// <summary>Two cues with a gap between them, and room before and after.</summary>
    private static SubtitleDocument TwoCues() => new([Cue(1, 3, "Un"), Cue(5, 8, "Deux")]);

    // ---- Normalization -------------------------------------------------------------------------------------

    [Fact]
    public void Cues_are_sorted_and_overlaps_are_cut()
    {
        var document = new SubtitleDocument([Cue(5, 8, "b"), Cue(1, 6, "a")]);

        document.Cues.ShouldBe([Cue(1, 6, "a"), Cue(6, 8, "b")]);
    }

    [Fact]
    public void A_cue_is_at_least_the_minimum_duration()
    {
        new SubtitleDocument([Cue(1, 1, "a")]).Cues.ShouldBe([new SubtitleCue(S(1), S(1) + SubtitleDocument.MinDuration, "a")]);
    }

    // ---- Timeline and synchronization ----------------------------------------------------------------------

    [Fact]
    public void Timeline_alternates_cues_and_the_gaps_between_them_up_to_the_end_of_the_video()
    {
        TwoCues().Timeline(S(10)).ShouldBe(
        [
            new GapEntry(S(0), S(1)),
            new CueEntry(0, Cue(1, 3, "Un")),
            new GapEntry(S(3), S(5)),
            new CueEntry(1, Cue(5, 8, "Deux")),
            new GapEntry(S(8), S(10))
        ]);
    }

    [Fact]
    public void Timeline_has_no_empty_gap_between_touching_cues()
    {
        new SubtitleDocument([Cue(0, 2), Cue(2, 4)]).Timeline(S(4)).Select(e => e.GetType().Name)
            .ShouldBe(["CueEntry", "CueEntry"]);
    }

    [Fact]
    public void Timeline_without_cues_is_one_gap_over_the_whole_video()
    {
        new SubtitleDocument([]).Timeline(S(12)).ShouldBe([new GapEntry(S(0), S(12))]);
    }

    [Fact]
    public void Timeline_of_an_empty_document_before_the_duration_is_known_is_empty()
    {
        new SubtitleDocument([]).Timeline(TimeSpan.Zero).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 0)]      // before the first cue: the gap before it
    [InlineData(0.99, 0)]
    [InlineData(1, 1)]      // a cue starts at its start…
    [InlineData(2.99, 1)]
    [InlineData(3, 2)]      // …and ends just before its end: then the gap between two cues
    [InlineData(4.5, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 4)]      // after the last cue
    [InlineData(10, 4)]     // at the very end of the video
    [InlineData(25, 4)]     // past it
    [InlineData(-1, 0)]
    public void IndexAt_finds_the_cue_or_the_gap_at_a_position(double seconds, int expected)
    {
        var timeline = TwoCues().Timeline(S(10));

        SubtitleDocument.IndexAt(timeline, S(seconds)).ShouldBe(expected);
    }

    [Fact]
    public void IndexAt_of_an_empty_timeline_is_minus_one() => SubtitleDocument.IndexAt([], S(1)).ShouldBe(-1);

    // ---- Editing -------------------------------------------------------------------------------------------

    [Fact]
    public void SetText_trims_the_text_and_removes_blank_lines_which_srt_cannot_hold()
    {
        var document = TwoCues();

        document.SetText(0, "  Première ligne \n\n  seconde ligne  \n").ShouldBeTrue();

        document.Cues[0].Text.ShouldBe("Première ligne\nseconde ligne");
    }

    [Fact]
    public void SetText_with_the_same_text_changes_nothing()
    {
        var document = TwoCues();

        document.SetText(0, "Un").ShouldBeFalse();
        document.History.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public void SetStart_and_SetEnd_move_a_cue_within_its_neighbours()
    {
        var document = TwoCues();

        document.SetStart(1, S(4)).ShouldBeNull();
        document.SetEnd(0, S(4)).ShouldBeNull();

        document.Cues.ShouldBe([Cue(1, 4, "Un"), Cue(4, 8, "Deux")]);
    }

    [Fact]
    public void SetStart_refuses_to_overlap_the_previous_cue()
    {
        var document = TwoCues();

        document.SetStart(1, S(2.5)).ShouldBe("Le début chevauche le sous-titre précédent, qui finit à 00:03.000.");
        document.Cues[1].Start.ShouldBe(S(5));
    }

    [Fact]
    public void SetEnd_refuses_to_overlap_the_next_cue()
    {
        TwoCues().SetEnd(0, S(6)).ShouldBe("La fin chevauche le sous-titre suivant, qui commence à 00:05.000.");
    }

    [Fact]
    public void A_cue_cannot_be_shorter_than_the_minimum_or_end_before_it_starts()
    {
        var document = TwoCues();

        document.SetEnd(0, S(1.05)).ShouldBe("Un sous-titre dure au moins 0,1 s.");
        document.SetStart(0, S(3)).ShouldBe("Un sous-titre dure au moins 0,1 s.");
    }

    [Fact]
    public void Remove_deletes_a_cue()
    {
        var document = TwoCues();

        document.Remove(0);

        document.Cues.ShouldBe([Cue(5, 8, "Deux")]);
    }

    [Fact]
    public void AddInGap_adds_an_empty_cue_at_the_start_of_the_gap_lasting_at_most_two_seconds()
    {
        var document = TwoCues();

        document.AddInGap(new GapEntry(S(8), S(20))).ShouldBe(2);
        document.AddInGap(new GapEntry(S(3), S(4.5))).ShouldBe(1);

        document.Cues.ShouldBe([Cue(1, 3, "Un"), Cue(3, 4.5, ""), Cue(5, 8, "Deux"), Cue(8, 10, "")]);
    }

    [Fact]
    public void AddInGap_refuses_a_gap_too_short_for_a_cue_or_overlapping_one()
    {
        var document = TwoCues();

        document.AddInGap(new GapEntry(S(3), S(3.05))).ShouldBe(-1);
        document.AddInGap(new GapEntry(S(2), S(4))).ShouldBe(-1);
        document.Cues.Count.ShouldBe(2);
    }

    [Fact]
    public void Each_edit_can_be_undone_and_redone()
    {
        var document = TwoCues();
        var original = document.Cues.ToList();
        document.SetText(0, "Changé");
        document.SetEnd(1, S(9));
        document.Remove(0);

        document.History.Undo();
        document.History.Undo();
        document.History.Undo();
        document.Cues.ShouldBe(original);

        document.History.Redo();
        document.Cues[0].Text.ShouldBe("Changé");
    }

    [Fact]
    public void Changed_is_raised_by_edits_and_by_undo()
    {
        var document = TwoCues();
        var changes = 0;
        document.Changed += (_, _) => changes++;

        document.SetText(0, "Changé");
        document.SetStart(0, S(9)); // refused
        document.History.Undo();

        changes.ShouldBe(2);
    }
}
