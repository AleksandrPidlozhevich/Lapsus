using System.Text;
using System.Text.RegularExpressions;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Speech;

// Puts English terms back into Latin after Whisper wrote them the way they sound: «пулл реквест» → "pull
// request", «гитхаб» → "GitHub", «ребейс» → "rebase". The prompt already gets most terms right; this is the
// second pass, and it is deliberately timid, because turning a real word into English is worse than leaving a
// term in Cyrillic:
//  - a word the language's dictionary knows stays (компьютер, файл, мастер, релиз), unless the user put it in
//    their glossary — or, for a built-in term, the word is rare (зум → Zoom);
//  - an inflected form stays (задеплоил, ребейса, коммитов): the last sound must match the term's;
//  - a match needs the same consonants in the same order and a close vowel spelling.
// Candidates come from the user's glossary, the built-in list (Speech/Data/terms.txt) and — for single words
// only — the English frequency list. Without a dictionary for the language, only the two lists are used: there
// is no telling a real word from a term then.
public sealed partial class TermRestorer
{
    // A built-in term may replace a word the dictionary knows if the word is rarer than this
    // (SpellChecker.Frequency, 0..1). зум ≈ 0.37, сервер ≈ 0.42, файл ≈ 0.5, мастер ≈ 0.55.
    public const double RareWordFrequency = 0.4;

    // English list words rarer than this count are mostly names and noise.
    private const long MinEnglishCount = 50;

    private const int MaxPhraseWords = 3;

    private enum Source { Glossary, BuiltIn, English }

    private sealed record Entry(string Text, string[] Parts, Source Source, double Weight)
    {
        public string Key { get; } = string.Concat(Parts);
    }

    private readonly SpellChecker _spell;
    private readonly Dictionary<string, List<Entry>> _terms = new(StringComparer.Ordinal);
    private readonly Lazy<Dictionary<string, List<Entry>>> _english;

    private static readonly Lazy<IReadOnlyList<(string Canonical, string[] Heard)>> BuiltIn = new(LoadBuiltIn);

    public TermRestorer(SpellChecker spell, IEnumerable<string>? glossary = null, bool builtInTerms = true)
    {
        _spell = spell;
        if (builtInTerms)
            foreach (var (canonical, heard) in BuiltIn.Value)
                AddTerm(canonical, heard, Source.BuiltIn);

        foreach (var line in glossary ?? [])
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                AddTerm(parts[0], parts[1..], Source.Glossary);
        }

        _english = new Lazy<Dictionary<string, List<Entry>>>(BuildEnglishIndex);
    }

    public string Restore(string text, string language)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var words = Word().Matches(text).ToList();
        var replacements = new List<(int Start, int End, string Text)>();

        // In a language written in Latin there is no script to tell a term by, and "slack" may be just a word:
        // only the user's own glossary applies there.
        var glossaryOnly = DictationLanguagePicker.ScriptOf(language) == Script.Latin;

        for (var i = 0; i < words.Count;)
        {
            var taken = TryReplace(text, words, i, language, glossaryOnly, out var replacement);
            if (taken > 0)
            {
                var start = words[i].Index;
                var last = words[i + taken - 1];
                replacements.Add((start, last.Index + last.Length, replacement!));
                i += taken;
            }
            else
            {
                i++;
            }
        }

        if (replacements.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length + 16);
        var at = 0;
        foreach (var (start, end, value) in replacements)
        {
            sb.Append(text, at, start - at).Append(value);
            at = end;
        }
        return sb.Append(text, at, text.Length - at).ToString();
    }

    // How many words starting at i were replaced (0 for none), longest phrase first.
    private int TryReplace(
        string text, List<Match> words, int i, string language, bool glossaryOnly, out string? replacement)
    {
        replacement = null;
        var first = words[i].Value;
        var latin = Scripts.Dominant(first) == Script.Latin;

        for (var n = Math.Min(MaxPhraseWords, words.Count - i); n >= 1; n--)
        {
            if (!Adjacent(text, words, i, n, latin))
                continue;

            var parts = Enumerable.Range(i, n).Select(k => PhoneticKey.Heard(words[k].Value)).ToArray();
            if (parts.Any(p => p.Length == 0) || string.Concat(parts).Length < 2)
                continue;

            if (Best(_terms, parts, glossaryOnly) is { } term && MayReplace(words, i, n, term, language, latin))
            {
                replacement = Cased(first, term);
                return n;
            }
        }

        // Single words against the English list: only a word of another script the dictionary does not know.
        if (glossaryOnly || latin || first.Length < 4 || !KnowsLanguage(first, out var script) || Knows(first, script, language))
            return 0;

        var key = PhoneticKey.Heard(first);
        if (key.Length < 4 || Best(_english.Value, [key]) is not { } word)
            return 0;

        replacement = Cased(first, word);
        return 1;
    }

    private bool MayReplace(List<Match> words, int i, int n, Entry term, string language, bool latin)
    {
        if (latin)
        {
            // Latin already: only re-spell a garbled or mis-cased term ("Hatfix", "github"), never a real word.
            var asWritten = string.Join(' ', words.Skip(i).Take(n).Select(w => w.Value));
            return !string.Equals(asWritten, term.Text, StringComparison.Ordinal) &&
                   (string.Equals(asWritten, term.Text, StringComparison.OrdinalIgnoreCase)
                       ? term.Text.Any(char.IsUpper)
                       : n > 1 || !_spell.IsKnownInLanguage(words[i].Value, "en"));
        }

        if (term.Source == Source.Glossary || n > 1)
            return true;

        var word = words[i].Value;
        if (!KnowsLanguage(word, out var script))
            return true;
        return !Knows(word, script, language) || _spell.Frequency(word, script, language) < RareWordFrequency;
    }

    private bool KnowsLanguage(string word, out Script script)
    {
        script = Scripts.Dominant(word) ?? Script.Latin;
        return script != Script.Latin && _spell.Has(script);
    }

    private bool Knows(string word, Script script, string language) =>
        _spell.IsKnownWord(word, script, language) ||
        _spell.IsLexiconWord(word, script, language, typedInScript: true) ||
        _spell.IsLexiconWord(word, script, typedInScript: true);

    // Words i..i+n-1 of one script kind, separated only by spaces or hyphens.
    private static bool Adjacent(string text, List<Match> words, int i, int n, bool latin)
    {
        for (var k = i; k < i + n; k++)
        {
            if ((Scripts.Dominant(words[k].Value) == Script.Latin) != latin)
                return false;
            if (k == i)
                continue;

            var gapStart = words[k - 1].Index + words[k - 1].Length;
            for (var c = gapStart; c < words[k].Index; c++)
                if (text[c] is not (' ' or '-'))
                    return false;
        }
        return true;
    }

    // The closest entry with as many words as the window, each word sounding close to the term's word: «а
    // фронтенд» must not become "frontend". Ties go to the glossary, then to the commoner word.
    private static Entry? Best(Dictionary<string, List<Entry>> index, string[] heard, bool glossaryOnly = false)
    {
        var whole = string.Concat(heard);
        if (!index.TryGetValue(PhoneticKey.Skeleton(whole), out var entries))
            return null;

        Entry? best = null;
        var bestDistance = int.MaxValue;
        foreach (var entry in entries)
        {
            if (entry.Parts.Length != heard.Length || (glossaryOnly && entry.Source != Source.Glossary))
                continue;

            var distance = 0;
            for (var k = 0; k < heard.Length && distance != int.MaxValue; k++)
                distance = Close(entry.Parts[k], heard[k]) is { } d ? distance + d : int.MaxValue;
            if (distance == int.MaxValue)
                continue;

            if (distance < bestDistance || (distance == bestDistance && entry.Weight > best!.Weight))
            {
                best = entry;
                bestDistance = distance;
            }
        }
        return best;
    }

    // How far a heard word is from a term's word, or null when they are not the same word: the consonants must
    // agree in order, the last sound must agree (so «ребейса» is not "rebase"), and the vowels may differ only a
    // little.
    private static int? Close(string term, string heard)
    {
        if (term.Length == 0 || heard.Length == 0 || term[^1] != heard[^1] ||
            PhoneticKey.Skeleton(term) != PhoneticKey.Skeleton(heard))
            return null;

        var distance = Distance(term, heard);
        return distance <= Allowed(Math.Max(term.Length, heard.Length)) ? distance : null;
    }
    // Exact for short keys, where one sound decides the word; one or two differences for longer ones.
    private static int Allowed(int keyLength) => keyLength <= 4 ? 0 : keyLength <= 7 ? 1 : 2;

    // Brand and acronym spellings are kept as listed; a plain word follows the capital of the word it replaces.
    private static string Cased(string original, Entry entry)
    {
        var text = entry.Text;
        if (text.Any(char.IsUpper) || original.Length == 0 || !char.IsUpper(original[0]))
            return text;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private void AddTerm(string canonical, IEnumerable<string> heard, Source source)
    {
        var weight = source == Source.Glossary ? 2.0 : 1.0;
        // ".NET", "C#", "Node.js": the letters alone do not say the term (".NET" would read as «нет»), so such
        // terms are found by their listed spoken forms only.
        if (canonical.All(c => char.IsLetter(c) || c is ' ' or '-'))
        {
            var parts = Word().Matches(canonical);
            Add(_terms, new Entry(canonical, parts.Select(m => PhoneticKey.English(m.Value)).ToArray(), source,
                weight), minSkeleton: 1);
        }
        foreach (var form in heard)
        {
            var parts = Word().Matches(form);
            Add(_terms, new Entry(canonical, parts.Select(m => PhoneticKey.Heard(m.Value)).ToArray(), source,
                weight), minSkeleton: 1);
        }
    }

    private Dictionary<string, List<Entry>> BuildEnglishIndex()
    {
        var index = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        foreach (var (word, count) in _spell.WordsIn("en"))
        {
            if (count < MinEnglishCount || word.Length < 3 || !word.All(c => c is >= 'a' and <= 'z'))
                continue;
            Add(index, new Entry(word, [PhoneticKey.English(word)], Source.English, Math.Log(1 + count)), minSkeleton: 2);
        }
        return index;
    }

    // Listed terms may be as short as a spelled-out acronym («си ай»); English list words need two consonants,
    // or every short Russian word would find an English twin.
    private static void Add(Dictionary<string, List<Entry>> index, Entry entry, int minSkeleton)
    {
        if (entry.Key.Length < 2 || entry.Parts.Any(p => p.Length == 0))
            return;

        var skeleton = PhoneticKey.Skeleton(entry.Key);
        if (skeleton.Length < minSkeleton)
            return;

        if (!index.TryGetValue(skeleton, out var list))
            index[skeleton] = list = [];
        list.Add(entry);
    }

    private static IReadOnlyList<(string, string[])> LoadBuiltIn()
    {
        return SpeechData.Lines("terms.txt")
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length > 0)
            .Select(parts => (parts[0], parts[1..]))
            .ToList();
    }

    private static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    // Letters, with marks and inner apostrophes; ".NET" and "C#" keep their symbols when they are the term.
    [GeneratedRegex(@"[\p{L}\p{M}](?:[\p{L}\p{M}\p{Nd}]|['’](?=\p{L}))*")]
    private static partial Regex Word();
}
