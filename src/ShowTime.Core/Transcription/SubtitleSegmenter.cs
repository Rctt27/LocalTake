namespace ShowTime.Core.Transcription;

/// <summary>
/// Cuts the words Whisper heard into subtitles, as a subtitler would: a silence ends a cue; within a stretch of speech,
/// the cuts that cost least are chosen together, a cue ending at the end of a sentence, then at a comma, then before a
/// clause or a preposition, and never after an article, a preposition or a subject pronoun; cues too short to read,
/// too long to see, or on two lines cost more.
/// </summary>
public static class SubtitleSegmenter
{
    // The costs weigh the rules against one another: a cue shorter than the minimum is worse than a second line,
    // which is worse than a cut between two ordinary words.
    private const double EachCueCost = 1;
    private const double SecondLineCost = 3;
    private const double TooShortCost = 4;
    private const double SentenceEndCut = 0;
    private const double PunctuationCut = 0.5;
    private const double ClauseCut = 1;
    private const double PlainCut = 2;
    private const double GlueCut = 6;

    /// <summary>The weight of the length difference between two lines, per character.</summary>
    private const double LineImbalanceCost = 0.1;

    /// <summary>
    /// The weight of each character past the profile's target length, per line: a line of 42 characters, 10 past a
    /// target of 32, costs as much as one more cue with a good cut, so a sentence goes in groups of words.
    /// </summary>
    private const double OverTargetCost = 0.2;

    /// <summary>What Whisper writes over silence or music, from the credits of the videos it was trained on.</summary>
    private static readonly string[] Hallucinations =
    [
        "amara.org", "sous-titrage st'", "sous-titres réalisés par", "sous-titrage société radio-canada"
    ];

    /// <summary>Words that lean on the next one: articles, determiners, prepositions, subject and object pronouns.</summary>
    private static readonly HashSet<string> Glue = new(StringComparer.OrdinalIgnoreCase)
    {
        "le", "la", "les", "l'", "un", "une", "des", "du", "de", "d'", "au", "aux", "à", "en", "dans", "sur", "sous", "par",
        "pour", "avec", "sans", "chez", "vers", "entre", "ce", "cet", "cette", "ces", "mon", "ma", "mes", "ton", "ta",
        "tes", "son", "sa", "ses", "notre", "nos", "votre", "vos", "leur", "leurs", "je", "j'", "tu", "il", "elle", "on",
        "nous", "vous", "ils", "elles", "ne", "n'", "se", "s'", "me", "m'", "te", "t'", "qui", "que", "qu'", "dont",
        "the", "a", "an", "to", "of", "in", "on", "at", "for", "with", "from", "by", "my", "your", "our", "their",
        "i", "you", "we", "they", "he", "she"
    };

    /// <summary>Words that open a clause or a phrase: a cue may start with them.</summary>
    private static readonly HashSet<string> Openers = new(StringComparer.OrdinalIgnoreCase)
    {
        "et", "mais", "ou", "donc", "or", "ni", "car", "puis", "alors", "ensuite", "enfin", "qui", "que", "qu'", "dont",
        "où", "quand", "lorsque", "parce", "si", "comme", "pour", "avec", "dans", "sur", "sous", "par", "sans", "chez",
        "de", "du", "des", "d'", "à", "au", "aux", "en",
        "vers", "entre", "depuis", "pendant", "avant", "après", "and", "but", "or", "so", "because", "which", "that",
        "who", "when", "where", "if", "then", "with", "for", "to", "from", "in", "on", "of", "at", "by"
    };

    public static IReadOnlyList<SubtitleCue> Segment(IReadOnlyList<TranscribedWord> words, SubtitleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(profile);

        var blocks = new List<List<TranscribedWord>>();
        foreach (var stretch in Stretches(words, profile))
        {
            var spoken = WithoutNonSpeech(stretch);
            if (spoken.Count > 0)
                blocks.AddRange(Cut(spoken, profile));
        }
        return Time(blocks, profile);
    }

    /// <summary>
    /// What Whisper writes but nobody said: a sound between brackets ("[Musique]", "(rires)"), or a sentence of the
    /// credits it learnt from subtitled videos. Dropped by themselves: Whisper often glues them to the last words, without
    /// a pause, and dropping the whole stretch lost everything that was said.
    /// </summary>
    private static List<TranscribedWord> WithoutNonSpeech(List<TranscribedWord> words)
    {
        var kept = new List<TranscribedWord>();
        var sentence = new List<TranscribedWord>();
        void EndSentence()
        {
            var text = Join(sentence);
            if (!Hallucinations.Any(h => text.Contains(h, StringComparison.OrdinalIgnoreCase)))
                kept.AddRange(sentence);
            sentence.Clear();
        }

        var inBrackets = false;
        foreach (var word in words)
        {
            if (!inBrackets && word.Text[0] is '[' or '(')
                inBrackets = true;
            if (inBrackets)
            {
                if (word.Text[^1] is ']' or ')')
                    inBrackets = false;
                continue;
            }
            sentence.Add(word);
            if (EndsSentence(word.Text))
                EndSentence();
        }
        EndSentence();
        return kept;
    }

    /// <summary>Trimmed words, grouped between the silences that end a cue whatever it says.</summary>
    private static List<List<TranscribedWord>> Stretches(IReadOnlyList<TranscribedWord> words, SubtitleProfile profile)
    {
        var stretches = new List<List<TranscribedWord>>();
        List<TranscribedWord>? current = null;
        foreach (var word in words.OrderBy(w => w.Start))
        {
            var text = word.Text.Trim();
            if (text.Length == 0)
                continue;
            if (current is null || word.Start - current[^1].End >= profile.PauseSplit)
                stretches.Add(current = []);
            current.Add(word with { Text = text });
        }
        return stretches;
    }

    /// <summary>The cheapest way to cut a stretch of speech into cues, all the cuts weighed together.</summary>
    private static List<List<TranscribedWord>> Cut(List<TranscribedWord> words, SubtitleProfile profile)
    {
        // best[i]: the cost of the words before i; from[i]: where the last cue of that solution starts.
        var best = new double[words.Count + 1];
        var from = new int[words.Count + 1];
        Array.Fill(best, double.PositiveInfinity);
        best[0] = 0;
        for (var end = 1; end <= words.Count; end++)
        {
            for (var start = end - 1; start >= 0; start--)
            {
                var cost = CueCost(words, start, end, profile);
                if (cost is { } value && best[start] + value < best[end])
                {
                    best[end] = best[start] + value;
                    from[end] = start;
                }
            }
        }

        var cues = new List<List<TranscribedWord>>();
        for (var end = words.Count; end > 0; end = from[end])
            cues.Insert(0, words.GetRange(from[end], end - from[end]));
        return cues;
    }

    /// <summary>The cost of a cue holding words[start..end), or null when it breaks the profile.</summary>
    private static double? CueCost(List<TranscribedWord> words, int start, int end, SubtitleProfile profile)
    {
        var single = end - start == 1;
        var duration = words[end - 1].End - words[start].Start;
        var length = Join(words, start, end).Length;
        if (!single && (duration > profile.MaxDuration || length > profile.MaxLineLength * profile.MaxLines))
            return null;

        var cost = EachCueCost + (end == words.Count ? SentenceEndCut : CutCost(words, end));
        if (duration < profile.MinDuration)
            cost += TooShortCost;
        if (length <= profile.MaxLineLength || single)
            return cost + OverTarget(length, profile);
        if (LineBreak(words, start, end, profile) is not { } lineBreak)
            return null;
        return cost + SecondLineCost + lineBreak.Cost / 2 +
               OverTarget(Join(words, start, lineBreak.At).Length, profile) +
               OverTarget(Join(words, lineBreak.At, end).Length, profile);
    }

    private static double OverTarget(int lineLength, SubtitleProfile profile) =>
        Math.Max(0, lineLength - profile.TargetLineLength) * OverTargetCost;

    /// <summary>How bad it is to cut before words[at].</summary>
    private static double CutCost(List<TranscribedWord> words, int at)
    {
        var before = words[at - 1].Text;
        if (EndsSentence(before))
            return SentenceEndCut;
        if (before[^1] is ',' or ';' or ':')
            return PunctuationCut;
        if (Glue.Contains(Bare(before)))
            return GlueCut;
        return Openers.Contains(Bare(words[at].Text)) ? ClauseCut : PlainCut;
    }

    private static bool EndsSentence(string word) => word[^1] is '.' or '?' or '!' or '…';

    /// <summary>A word without the punctuation around it, to look it up in the word lists.</summary>
    private static string Bare(string word) => word.Trim(',', ';', ':', '.', '?', '!', '…', '"', '«', '»', ' ');

    /// <summary>Where to break a two-line cue: both lines fit, at a good cut, the lines of about the same length.</summary>
    private static (int At, double Cost)? LineBreak(List<TranscribedWord> words, int start, int end, SubtitleProfile profile)
    {
        (int At, double Cost)? best = null;
        for (var at = start + 1; at < end; at++)
        {
            int top = Join(words, start, at).Length, bottom = Join(words, at, end).Length;
            if (top > profile.MaxLineLength || bottom > profile.MaxLineLength)
                continue;
            var cost = CutCost(words, at) + Math.Abs(top - bottom) * LineImbalanceCost;
            if (best is null || cost < best.Value.Cost)
                best = (at, cost);
        }
        return best;
    }

    /// <summary>
    /// The cues' times: from the first word to the last, then kept up to the minimum duration and lingering a little
    /// when the next cue leaves room, within the maximum duration.
    /// </summary>
    private static List<SubtitleCue> Time(List<List<TranscribedWord>> blocks, SubtitleProfile profile)
    {
        var cues = new List<SubtitleCue>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var words = blocks[i];
            var start = words[0].Start;
            var spoken = words[^1].End;
            var limit = start + profile.MaxDuration;
            if (i + 1 < blocks.Count && blocks[i + 1][0].Start - profile.MinGap < limit)
                limit = blocks[i + 1][0].Start - profile.MinGap;
            var end = Max(spoken, Min(Max(start + profile.MinDuration, spoken + profile.Linger), limit));
            cues.Add(new SubtitleCue(start, end, Text(words, profile)));
        }
        return cues;
    }

    private static string Text(List<TranscribedWord> words, SubtitleProfile profile)
    {
        var text = Join(words);
        if (text.Length <= profile.MaxLineLength || words.Count == 1 || LineBreak(words, 0, words.Count, profile) is not { } lineBreak)
            return text;
        return Join(words, 0, lineBreak.At) + "\n" + Join(words, lineBreak.At, words.Count);
    }

    private static string Join(List<TranscribedWord> words) => Join(words, 0, words.Count);

    private static string Join(List<TranscribedWord> words, int start, int end) =>
        string.Join(" ", words.Skip(start).Take(end - start).Select(w => w.Text));

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
