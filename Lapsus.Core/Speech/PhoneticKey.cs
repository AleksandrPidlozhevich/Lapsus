using System.Text;
using System.Text.RegularExpressions;
using Lapsus.Core.Layout;
using Lapsus.Core.Text;

namespace Lapsus.Core.Speech;

// A rough spelling of how a word sounds, in Latin letters, so that an English term and the way Whisper wrote
// it in another script meet: "request" and «реквест» both become "rekvest", "merge" and «мердж» "merJ",
// "rebase" and «ребейс» "rebeis". Upper-case letters mark sounds Latin spells in two letters:
// J = dʒ/ʒ, S = ʃ, C = tʃ. It is deliberately coarse — no stress, z = s, h dropped — and only ever compared
// together with the consonant skeleton and the last sound (see TermRestorer).
public static partial class PhoneticKey
{
    public static string English(string word)
    {
        var w = new string(word.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z').ToArray());
        if (w.Length == 0)
            return "";

        w = w.Replace("dge", "J").Replace("dg", "J").Replace("gg", "G");
        // Soft g only where English is near-regular: merge, stage-s/-d/-r, staging, engine.
        w = SoftG().Replace(w, "J");
        w = w.Replace('j', 'J');
        w = MagicE(w);
        w = w.Replace("tch", "C").Replace("sch", "sk").Replace("tion", "Sn").Replace("sion", "Sn")
             .Replace("ture", "Cer");
        w = w.Length > 2 && w.IndexOf("gh", 1, StringComparison.Ordinal) > 0
            ? w[..1] + w[1..].Replace("gh", "")
            : w;
        w = w.Replace("ph", "f").Replace("ck", "k").Replace("qu", "kv").Replace("wh", "v")
             .Replace("sh", "S").Replace("ch", "C").Replace("th", "t");
        w = SoftC().Replace(w, "s").Replace('c', 'k');
        w = w.Replace("oo", "u").Replace("ee", "i").Replace("ea", "i").Replace("ai", "ei").Replace("ay", "ei")
             .Replace("ou", "au").Replace("ew", "yu");
        if (w.EndsWith("ow", StringComparison.Ordinal))
            w = w[..^2] + "ou";
        if (w.Length > 3 && w.EndsWith("ue", StringComparison.Ordinal))
            w = w[..^1];
        if (w.Length > 3 && w[^1] == 'e' && !IsVowel(w[^2]))
            w = w[..^1];

        return Finish(w);
    }

    // How a word written in a non-Latin script (or already in Latin, as heard) sounds.
    public static string Heard(string word)
    {
        var script = Scripts.Dominant(word);
        if (script is null)
            return "";

        if (script == Script.Latin)
            return English(word);
        if (script == Script.Georgian)
            return "";

        // No language on purpose: the general Cyrillic table reads Ukrainian «г» in «гітхаб» as g, which is what
        // it stands for in a borrowed English word.
        var latin = Transliterator.Convert(word.ToLowerInvariant()).ToLowerInvariant();
        latin = latin.Replace("dzh", "J").Replace("shch", "S").Replace("zh", "J").Replace("sh", "S")
                     .Replace("ch", "C").Replace("kh", "h").Replace('j', 'y');
        latin = new string(latin.Where(c => c is >= 'a' and <= 'z' or 'J' or 'S' or 'C').ToArray());
        return Finish(latin);
    }

    // Consonants only, in order: the part of a word sound survives best between scripts.
    public static string Skeleton(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var c in key)
            if (!IsVowel(c))
                sb.Append(c);
        return sb.ToString();
    }

    public static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    // rebase → rebeis, file → fail, code → kod: a single vowel before consonant + final e says its name.
    private static string MagicE(string w)
    {
        if (w.Length < 4 || w[^1] != 'e' || IsVowel(w[^2]))
            return w;

        var vowelAt = w.Length - 3;
        if (!IsVowel(w[vowelAt]) || (vowelAt > 0 && IsVowel(w[vowelAt - 1])))
            return w;

        // Upper case so the digraph rules that follow leave these alone; Finish lowers them.
        var said = w[vowelAt] switch
        {
            'a' => "EI",
            'i' => "AI",
            'u' => "YU",
            var v => v.ToString(),
        };
        return w[..vowelAt] + said + w[^2];
    }

    private static string Finish(string w)
    {
        w = w.Replace('A', 'a').Replace('E', 'e').Replace('I', 'i').Replace('Y', 'y').Replace('U', 'u');
        w = w.Replace("x", "ks").Replace('w', 'v').Replace('q', 'k').Replace('z', 's').Replace('c', 'k')
             .Replace('G', 'g').Replace("h", "");

        // y: a consonant before a vowel at the start, a vowel (i) everywhere else.
        var sb = new StringBuilder(w.Length);
        for (var i = 0; i < w.Length; i++)
        {
            var c = w[i];
            if (c == 'y')
                c = i + 1 < w.Length && IsVowel(w[i + 1]) && (i == 0 || IsVowel(w[i - 1])) ? 'y' : 'i';
            if (sb.Length > 0 && sb[^1] == c)
                continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    [GeneratedRegex("g(?=e$|es$|ed$|er$|ing$|ine$)")]
    private static partial Regex SoftG();

    [GeneratedRegex("c(?=[eiy])")]
    private static partial Regex SoftC();
}
