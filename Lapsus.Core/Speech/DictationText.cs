namespace Lapsus.Core.Speech;

// Fits Whisper's text to the line it lands in. Whisper writes every recording as a sentence of its own,
// capital first, and does so even when told the sentence is under way (22 ru phrases after "Вчера я сказал
// коллеге, что": three in four still began with a capital).
public static class DictationText
{
    // before: the line up to the caret as far as it is known, "" when not; then nothing is changed.
    public static string FitAfter(string before, string text)
    {
        if (before.Length == 0 || text.Length == 0)
            return text;

        if (!EndsSentence(before))
            text = Decapitalize(text);

        return !char.IsWhiteSpace(before[^1]) && !char.IsWhiteSpace(text[0]) && !ClosesUp(text[0])
            ? " " + text
            : text;
    }

    private static bool EndsSentence(string before)
    {
        var i = before.Length - 1;
        while (i >= 0 && (char.IsWhiteSpace(before[i]) || IsClosing(before[i])))
            i--;
        return i < 0 || before[i] is '.' or '!' or '?' or '…' or ':';
    }

    // shortcut: a name or term that starts the text ("Москве", "Figma") is lowercased too; telling them apart
    // needs the dictionary's case, which the frequency lists do not keep.
    private static string Decapitalize(string text)
    {
        var end = 0;
        while (end < text.Length && char.IsLetter(text[end]))
            end++;
        var word = text[..end];
        if (word.Length == 0 || !char.IsUpper(word[0]) || word.Skip(1).Any(char.IsUpper) || word == "I")
            return text;
        return char.ToLowerInvariant(word[0]) + text[1..];
    }

    private static bool IsClosing(char ch) => ch is '"' or '\'' or ')' or ']' or '»' or '”' or '’';

    private static bool ClosesUp(char ch) => ch is '.' or ',' or '!' or '?' or '…' or ':' or ';' or ')' or ']' or '»';
}
