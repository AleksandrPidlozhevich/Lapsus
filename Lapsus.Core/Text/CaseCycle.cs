using Lapsus.Core.Layout;
using System.Text;

namespace Lapsus.Core.Text;

public static class CaseCycle
{
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

        if (hasLower && !hasUpper)
        {
            var sentence = FinalSigma(ToSentenceCase(text));
            if (!string.Equals(sentence, text, StringComparison.Ordinal))
                return sentence;
        }

        return text.ToUpperInvariant();
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
        return char.ToUpperInvariant(ch) != char.ToLowerInvariant(ch);
    }

    private static bool IsCaselessScript(char ch)
    {
        return Scripts.Of(ch) is { } script && Scripts.IsCaseless(script);
    }

    private static string ToSentenceCase(string text)
    {
        var result = new StringBuilder(text.Length);
        var atSentenceStart = true;

        foreach (var ch in text)
        {
            if (IsCased(ch))
            {
                var openSentence = atSentenceStart && !IsCaselessScript(ch);
                result.Append(openSentence ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                atSentenceStart = false;
                continue;
            }

            result.Append(ch);
            if (ch is '.' or '!' or '?' or '…' or '\n')
                atSentenceStart = true;
        }

        return result.ToString();
    }
}
