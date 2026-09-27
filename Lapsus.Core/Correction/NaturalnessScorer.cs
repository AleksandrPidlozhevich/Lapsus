using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public static class NaturalnessScorer
{
    private const double AbjadScore = 0.5;

    internal const double ImpossibleScore = 0.05;

    public static double Score(string text, Script script)
    {
        var possible = Orthography.IsPossibleWord(text, script);

        // Abjads write no vowels; vowel ratio would score every real word as gibberish.
        if (Scripts.IsAbjad(script))
        {
            if (!HasLetters(text, script))
                return 0.0;

            return possible ? AbjadScore : ImpossibleScore;
        }

        var score = VowelScore(text, script);
        return possible ? score : Math.Min(score, ImpossibleScore);
    }

    private static bool HasLetters(string text, Script script)
    {
        foreach (var ch in text)
            if (Alphabets.IsLetterOf(script, ch))
                return true;

        return false;
    }

    private static double VowelScore(string text, Script script)
    {
        var letters = 0;
        var vowels = 0;
        var currentRun = 0;
        var maxConsonantRun = 0;

        foreach (var ch in text)
        {
            if (!Alphabets.IsLetterOf(script, ch))
            {
                currentRun = 0;
                continue;
            }

            letters++;
            if (Alphabets.IsVowelOf(script, ch))
            {
                vowels++;
                currentRun = 0;
            }
            else
            {
                currentRun++;
                if (currentRun > maxConsonantRun)
                    maxConsonantRun = currentRun;
            }
        }

        if (letters == 0)
            return 0.0;

        var score = 1.0;
        var vowelRatio = (double)vowels / letters;

        // Three letters and no vowel: wrong-layout signature.
        if (letters >= 3 && vowels == 0)
        {
            score *= ImpossibleScore;
        }
        else
        {
            // Real words sit at roughly [0.2, 0.6] vowels.
            var drift = vowelRatio < 0.2 ? 0.2 - vowelRatio
                : vowelRatio > 0.6 ? vowelRatio - 0.6
                : 0.0;
            score *= Math.Max(0.1, 1.0 - drift * 2.0);
        }

        score *= maxConsonantRun switch
        {
            >= 5 => 0.2,
            4 => 0.6,
            _ => 1.0
        };

        return score;
    }
}
