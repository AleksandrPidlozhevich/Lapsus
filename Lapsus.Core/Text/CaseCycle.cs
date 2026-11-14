using Lapsus.Core.Layout;
using System.Text;

namespace Lapsus.Core.Text;

public static class CaseCycle
{
    // No one-letter capital: Unicode capitalises it as Եւ and ԵՒ.
    private const char ArmenianLigature = 'և';

    public static string Next(string text)
    {
        var hasUpper = false;
        var hasLower = false;
        foreach (var ch in text)
        {
            if (!IsCased(ch))
                continue;

            if (char.IsUpper(ch)) hasUpper = true;
            else hasLower = true;
        }

        if (!hasUpper && !hasLower)
            return text;

        if (hasUpper && !hasLower)
            return FinalSigma(text.ToLowerInvariant());

        // lower → Sentence → Every Word → UPPER → lower. No state is kept: the text says where it is, so
        // one word, where Sentence and Every Word look the same, goes straight on to UPPER.
        var sentence = FinalSigma(Capitalise(text, everyWord: false));
        if (hasLower && !hasUpper && !string.Equals(sentence, text, StringComparison.Ordinal))
            return sentence;

        if (hasUpper && string.Equals(sentence, text, StringComparison.Ordinal))
        {
            var title = FinalSigma(Capitalise(text, everyWord: true));
            if (!string.Equals(title, text, StringComparison.Ordinal))
                return title;
        }

        return text.ToUpperInvariant().Replace(ArmenianLigature.ToString(), "ԵՒ");
    }

    private static string FinalSigma(string text)
    {
        if (!text.Contains('σ'))
            return text;

        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] == 'σ' && (i + 1 == chars.Length || Scripts.Of(chars[i + 1]) != Script.Greek))
                chars[i] = 'ς';

        return new string(chars);
    }

    private static bool IsCased(char ch)
    {
        return ch == ArmenianLigature || char.ToUpperInvariant(ch) != char.ToLowerInvariant(ch);
    }

    private static bool IsCaselessScript(char ch)
    {
        return Scripts.Of(ch) is { } script && Scripts.IsCaseless(script);
    }

    private static string Capitalise(string text, bool everyWord)
    {
        var result = new StringBuilder(text.Length);
        var atSentenceStart = true;

        foreach (var ch in text)
        {
            if (IsCased(ch))
            {
                var openSentence = atSentenceStart && !IsCaselessScript(ch);
                if (openSentence && ch == ArmenianLigature)
                    result.Append("Եւ");
                else
                    result.Append(openSentence ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                atSentenceStart = false;
                continue;
            }

            result.Append(ch);
            // "5th" is a word already begun, not "5Th".
            if (everyWord && char.IsLetterOrDigit(ch))
                atSentenceStart = false;

            if (ch is '.' or '!' or '?' or '…' or '\n' or '։' || (everyWord && char.IsWhiteSpace(ch)))
                atSentenceStart = true;
        }

        return result.ToString();
    }
}
