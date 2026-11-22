using Lapsus.Core.Correction;

namespace Lapsus.Core.Speech;

// The neural brain's model as the judge of TermRestorer's loose guesses: it reads the dictated text as plain text with
// and without each term and takes the term only when that reads clearly likelier. It never writes text itself.
public sealed class DictationTermRanker(ILocalLlm llm)
{
    // Log-probability the term must win by.
    public const double Margin = 3.0;

    // Plain text after a line break, as ReadingRanker reads: nothing for the model to take as an instruction.
    private const string Context = "\n";

    public async Task<string> RankAsync(string text, IReadOnlyList<TermGuess> guesses, CancellationToken cancellationToken)
    {
        // Right to left, so the spans still to judge keep their offsets.
        foreach (var guess in guesses.OrderByDescending(g => g.Start))
        {
            var with = string.Concat(text.AsSpan(0, guess.Start), guess.Term, text.AsSpan(guess.Start + guess.Length));
            var scores = await llm.ScoreAsync(Context, [text, with], ends: true, cancellationToken).ConfigureAwait(false);
            if (scores is { Count: 2 } && scores[1] - scores[0] >= Margin)
                text = with;
        }
        return text;
    }
}
