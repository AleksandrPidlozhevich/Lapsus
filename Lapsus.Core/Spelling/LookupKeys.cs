using Lapsus.Core.Layout;
using WeCantSpell.Hunspell;

namespace Lapsus.Core.Spelling;

internal static class LookupKeys
{
    // Lookups fold marks; written-back text keeps the user's spelling. Hebrew keeps geresh.
    public static string For(string word, Script script)
    {
        var key = Scripts.FoldMarks(word.ToLowerInvariant(), script);
        if (script == Script.Arabic)
            key = FoldArabic(key);
        else if (script == Script.Cyrillic)
            key = StripInternalMarks(key, script);
        else if (script == Script.Armenian)
            key = SpellOutLigature(key);

        return key;
    }

    // The keyboard types the ligature և; Unilex and the Hunspell list spell it out.
    private static string SpellOutLigature(string word)
    {
        return word.Replace("և", "եւ");
    }

    // Romanian lists alone: ş/ţ (cedilla) → ș/ț (comma); Turkish ş is its own letter.
    public static string FoldCedilla(string key)
    {
        return key.Replace('ş', 'ș').Replace('ţ', 'ț');
    }

    // Do not fold alef maqsura to ya — merges short words and hands English strings a hit.
    private static string FoldArabic(string key)
    {
        var folded = key.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا');
        return folded.Length > 1 && folded[^1] == 'ة' ? string.Concat(folded.AsSpan(0, folded.Length - 1), "ه") : folded;
    }

    private static string StripInternalMarks(string word, Script script)
    {
        System.Text.StringBuilder? sb = null;
        for (var i = 0; i < word.Length; i++)
        {
            if (!Scripts.IsWordInternalMark(script, word[i]))
            {
                sb?.Append(word[i]);
                continue;
            }

            sb ??= new System.Text.StringBuilder(word, 0, i, word.Length);
        }

        return sb?.ToString() ?? word;
    }

    public static bool HasInternalMark(string word, Script script)
    {
        foreach (var ch in word)
            if (Scripts.IsWordInternalMark(script, ch))
                return true;

        return false;
    }

    public static bool InLexicon(WordList lexicon, string word)
    {
        // Keyboard apostrophe / cedilla / Armenian ligature variants must match dictionary spellings.
        return lexicon.Check(word) ||
               (word.Contains('ʼ') && lexicon.Check(word.Replace('ʼ', '\''))) ||
               (word.Contains('և') && lexicon.Check(SpellOutLigature(word))) ||
               (word.AsSpan().IndexOfAny("şţŞŢ") >= 0 &&
                lexicon.Check(word.Replace('ş', 'ș').Replace('ţ', 'ț').Replace('Ş', 'Ș').Replace('Ţ', 'Ț')));
    }

    public static string RestoreLeadingCase(string original, string suggestion)
    {
        if (original.Length == 0 || suggestion.Length == 0 || !char.IsUpper(original[0]))
            return suggestion;

        return char.ToUpperInvariant(suggestion[0]) + suggestion[1..];
    }
}
