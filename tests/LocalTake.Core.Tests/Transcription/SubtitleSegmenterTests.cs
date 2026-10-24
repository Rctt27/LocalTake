using LocalTake.Core.Transcription;

namespace LocalTake.Core.Tests.Transcription;

public sealed class SubtitleSegmenterTests
{
    private static readonly SubtitleProfile Tutorial = SubtitleProfile.Tutorial;

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>The words of a text, spoken one after the other from a start time, each lasting the same.</summary>
    private static List<TranscribedWord> Spoken(string text, double start, double perWord)
    {
        // Rounded to the millisecond, as Whisper gives them: summed doubles would be a tick off.
        return [.. text.Split(' ').Select((word, i) =>
            new TranscribedWord(S(Math.Round(start + i * perWord, 3)), S(Math.Round(start + (i + 1) * perWord, 3)), word))];
    }

    private static List<string> Texts(IEnumerable<SubtitleCue> cues) => cues.Select(c => c.Text).ToList();

    [Fact]
    public void A_short_sentence_is_one_cue_from_its_first_word_lingering_half_a_second_after_its_last()
    {
        var cues = SubtitleSegmenter.Segment(Spoken("Bonjour à tous.", 1, 0.4), Tutorial);

        cues.ShouldBe([new SubtitleCue(S(1), S(2.7), "Bonjour à tous.")]);
    }

    [Fact]
    public void A_pause_ends_the_cue_even_within_a_sentence_and_leaves_a_gap()
    {
        var words = Spoken("On clique ici", 0, 0.4).Concat(Spoken("puis là.", 3, 0.4)).ToList();

        var cues = SubtitleSegmenter.Segment(words, Tutorial);

        Texts(cues).ShouldBe(["On clique ici", "puis là."]);
        cues[0].End.ShouldBe(S(1.7));
        cues[1].Start.ShouldBe(S(3));
    }

    [Fact]
    public void A_long_sentence_is_cut_before_a_preposition_rather_than_leave_a_word_alone()
    {
        // In one line, "Démarrer." would be left alone for a quarter of a second.
        var cues = SubtitleSegmenter.Segment(Spoken("Il suffit ensuite de cliquer sur le bouton Démarrer.", 0, 0.25), Tutorial);

        Texts(cues).ShouldBe(["Il suffit ensuite de cliquer", "sur le bouton Démarrer."]);
    }

    [Fact]
    public void A_recorded_sentence_is_cut_into_short_groups_of_words_that_belong_together()
    {
        // The words of a real recording, as Whisper timed them. Cut into full lines, it gave
        // "je tente une nouvelle vidéo de la fenêtre / du terminal qui tourne sur Claude !".
        (double, double, string)[] heard =
        [
            (0.00, 0.42, "Salut"), (0.42, 0.88, "à"), (0.88, 1.10, "tous,"), (1.10, 1.30, "je"), (1.30, 1.69, "tente"),
            (1.69, 1.94, "une"), (1.94, 2.62, "nouvelle"), (2.62, 3.16, "vidéo"), (3.16, 3.43, "de"), (3.43, 3.58, "la"),
            (3.58, 4.44, "fenêtre"), (4.44, 4.63, "du"), (4.63, 5.60, "terminal"), (5.60, 5.70, "qui"), (5.70, 6.00, "tourne"),
            (6.00, 6.20, "sur"), (6.20, 8.50, "Claude"), (8.50, 8.50, "!")
        ];

        var cues = SubtitleSegmenter.Segment([.. heard.Select(w => new TranscribedWord(S(w.Item1), S(w.Item2), w.Item3))], Tutorial);

        Texts(cues).ShouldBe(["Salut à tous,", "je tente une nouvelle vidéo", "de la fenêtre du terminal", "qui tourne sur Claude !"]);
    }

    [Fact]
    public void A_clause_starts_a_new_cue()
    {
        var cues = SubtitleSegmenter.Segment(Spoken("D'abord, on choisit la zone de l'écran que vous voulez enregistrer.", 0, 0.3), Tutorial);

        Texts(cues).ShouldBe(["D'abord, on choisit la zone de l'écran", "que vous voulez enregistrer."]);
    }

    [Fact]
    public void A_cue_ends_at_the_end_of_a_sentence_when_the_next_one_follows_without_a_pause()
    {
        var cues = SubtitleSegmenter.Segment(Spoken("Voilà, c'est enregistré. Maintenant on passe au montage.", 0, 0.35), Tutorial);

        Texts(cues).ShouldBe(["Voilà, c'est enregistré.", "Maintenant on passe au montage."]);
    }

    [Fact]
    public void Fast_speech_takes_a_second_line_rather_than_cues_too_short_to_read()
    {
        // 51 characters in 1.6 s: two one-line cues would last 0.8 s each.
        var cues = SubtitleSegmenter.Segment(Spoken("Vous pouvez aussi choisir un autre micro dans la liste", 0, 0.16), Tutorial);

        Texts(cues).ShouldBe(["Vous pouvez aussi choisir\nun autre micro dans la liste"]);
    }

    [Fact]
    public void A_cue_never_ends_on_an_article_a_preposition_or_a_subject_pronoun_and_fits_the_profile()
    {
        var text = "Pour enregistrer une vidéo de votre écran avec le son du micro et le son de l'ordinateur, " +
                   "vous allez d'abord ouvrir le menu de LocalTake dans la zone de notification puis choisir la zone " +
                   "qui vous intéresse et enfin cliquer sur le bouton rouge qui se trouve en bas de la barre.";

        var cues = SubtitleSegmenter.Segment(Spoken(text, 0, 0.35), Tutorial);

        string[] glue = ["le", "la", "les", "un", "une", "de", "du", "des", "à", "au", "en", "sur", "avec", "dans", "vous", "je", "on", "se"];
        foreach (var cue in cues)
        {
            var last = cue.Text.Split(' ', '\n')[^1];
            glue.ShouldNotContain(last, cue.Text);
            cue.Text.Split('\n').ShouldAllBe(line => line.Length <= 42, cue.Text);
            (cue.End - cue.Start).ShouldBeLessThanOrEqualTo(Tutorial.MaxDuration, cue.Text);
        }
        string.Join(" ", cues.Select(c => c.Text.Replace('\n', ' '))).ShouldBe(text);
    }

    [Fact]
    public void Slow_speech_is_cut_before_the_maximum_duration()
    {
        // Under the pause length between words, but 0.9 s each: nine words would stay 8 s on screen.
        var words = new List<TranscribedWord>();
        for (var i = 0; i < 9; i++)
            words.Add(new TranscribedWord(S(i * 0.9), S(i * 0.9 + 0.6), "mot" + i));

        var cues = SubtitleSegmenter.Segment(words, Tutorial);

        cues.Count.ShouldBeGreaterThan(1);
        cues.ShouldAllBe(c => c.End - c.Start <= Tutorial.MaxDuration);
    }

    [Fact]
    public void A_short_cue_is_kept_at_least_a_second_when_there_is_room()
    {
        var words = Spoken("Ok.", 2, 0.3).Concat(Spoken("Ensuite on continue.", 5, 0.4)).ToList();

        var cues = SubtitleSegmenter.Segment(words, Tutorial);

        cues[0].ShouldBe(new SubtitleCue(S(2), S(3), "Ok."));
    }

    [Fact]
    public void Lingering_stops_shortly_before_the_next_cue()
    {
        // A pause just long enough to end the cue: no room for its full second.
        var words = Spoken("Ok.", 2, 0.3).Concat(Spoken("Ensuite on continue.", 2.9, 0.4)).ToList();

        var cues = SubtitleSegmenter.Segment(words, Tutorial);

        cues[0].End.ShouldBe(S(2.8));
        cues[1].Start.ShouldBe(S(2.9));
    }

    [Fact]
    public void What_Whisper_writes_over_silence_is_dropped()
    {
        var words = Spoken("[Musique]", 0, 1)
            .Concat(Spoken("Sous-titres réalisés par la communauté d'Amara.org", 3, 0.3))
            .Concat(Spoken("Bonjour.", 8, 0.5))
            .ToList();

        Texts(SubtitleSegmenter.Segment(words, Tutorial)).ShouldBe(["Bonjour."]);
    }

    [Fact]
    public void Credits_invented_right_after_the_last_words_drop_only_themselves()
    {
        // A real recording: Whisper wrote its Amara credits in the final silence, without a pause after the speech.
        // Judging the whole stretch, the segmenter dropped everything that was said.
        (double, double, string)[] heard =
        [
            (0.00, 1.48, "Bonjour"), (1.48, 2.71, "tout"), (2.71, 2.74, "le"), (2.74, 4.24, "monde,"), (4.24, 4.41, "je"),
            (4.41, 4.76, "fais"), (4.76, 5.02, "une"), (5.02, 5.72, "nouvelle"), (5.72, 6.28, "vidéo"), (6.28, 7.00, "pour"),
            (7.00, 7.60, "montrer"), (7.60, 9.22, "toujours"), (9.22, 9.79, "cette"), (9.79, 10.20, "même"),
            (10.20, 11.32, "fenêtre"), (11.32, 11.52, "de"), (11.52, 12.31, "terminal"), (12.31, 12.61, "qui"),
            (12.61, 13.10, "comme"), (13.10, 13.70, "vous"), (13.70, 14.03, "le"), (14.03, 14.57, "constatez"),
            (14.57, 15.03, "est"), (15.03, 15.66, "toujours"), (15.66, 15.86, "un"), (15.86, 16.31, "fond"),
            (16.31, 16.66, "noir"), (16.66, 17.06, "avec"), (17.06, 17.36, "des"), (17.36, 18.07, "textes"),
            (18.07, 19.12, "globalement"), (19.12, 19.48, "blancs"), (19.48, 19.78, "même"), (19.78, 19.90, "si"),
            (19.90, 20.38, "certains"), (20.38, 20.56, "ont"), (20.56, 20.74, "des"), (20.74, 21.22, "couleurs"),
            (21.22, 21.52, "un"), (21.52, 21.83, "peu"), (21.83, 22.37, "différentes,"), (22.37, 22.48, "il"),
            (22.48, 22.83, "faut"), (22.83, 22.97, "bien"), (22.97, 23.52, "l'avouer."), (23.52, 24.06, "[Sous-titres"),
            (24.06, 24.20, "par"), (24.20, 24.29, "la"), (24.29, 24.81, "communauté"), (24.81, 25.52, "d'Amara.org]")
        ];

        var cues = SubtitleSegmenter.Segment([.. heard.Select(w => new TranscribedWord(S(w.Item1), S(w.Item2), w.Item3))], Tutorial);

        string.Join(" ", cues.Select(c => c.Text.Replace('\n', ' '))).ShouldBe(
            "Bonjour tout le monde, je fais une nouvelle vidéo pour montrer toujours cette même fenêtre de terminal " +
            "qui comme vous le constatez est toujours un fond noir avec des textes globalement blancs même si certains " +
            "ont des couleurs un peu différentes, il faut bien l'avouer.");
    }

    [Fact]
    public void A_sound_between_brackets_drops_only_itself()
    {
        var words = Spoken("On clique ici, [Musique] puis on enregistre.", 0, 0.3);

        Texts(SubtitleSegmenter.Segment(words, Tutorial)).ShouldBe(["On clique ici, puis on enregistre."]);
    }

    [Fact]
    public void An_unbracketed_credit_drops_its_own_sentence_only()
    {
        var words = Spoken("Merci de votre attention. Sous-titrage ST' 501", 0, 0.3);

        Texts(SubtitleSegmenter.Segment(words, Tutorial)).ShouldBe(["Merci de votre attention."]);
    }

    [Fact]
    public void Blank_words_are_ignored_and_words_are_trimmed()
    {
        TranscribedWord[] words = [new(S(0), S(0.4), " Salut"), new(S(0.4), S(0.5), " "), new(S(0.5), S(0.9), " toi. ")];

        Texts(SubtitleSegmenter.Segment(words, Tutorial)).ShouldBe(["Salut toi."]);
    }

    [Fact]
    public void Nothing_said_gives_no_cue() => SubtitleSegmenter.Segment([], Tutorial).ShouldBeEmpty();

    [Fact]
    public void A_word_longer_than_a_line_is_still_shown()
    {
        var word = new string('x', 50);

        Texts(SubtitleSegmenter.Segment([new TranscribedWord(S(0), S(1), word)], Tutorial)).ShouldBe([word]);
    }
}
