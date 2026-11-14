using System.Text;

namespace Lapsus.Core.Layout;

// Korean is typed a letter (jamo) a key and the letters are gathered into syllable blocks by rule, not by
// choice: ㅇㅏㄴㄴㅕㅇ can only be 안녕. Compose is that rule as the two-set (dubeolsik) keyboard applies it;
// Decompose is its inverse, down to the keys.
public static class Hangul
{
    private const char FirstSyllable = '가';
    private const char LastSyllable = '힣';
    private const char FirstVowel = 'ㅏ';
    private const char LastVowel = 'ㅣ';
    private const int VowelCount = 21;
    private const int FinalCount = 28;

    private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";

    // Index 0 is "no final".
    private const string Finals = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

    // Two keys that make one letter: a vowel after a vowel, a final after a final.
    private static readonly Dictionary<(char, char), char> Pairs = new()
    {
        [('ㅗ', 'ㅏ')] = 'ㅘ', [('ㅗ', 'ㅐ')] = 'ㅙ', [('ㅗ', 'ㅣ')] = 'ㅚ',
        [('ㅜ', 'ㅓ')] = 'ㅝ', [('ㅜ', 'ㅔ')] = 'ㅞ', [('ㅜ', 'ㅣ')] = 'ㅟ', [('ㅡ', 'ㅣ')] = 'ㅢ',
        [('ㄱ', 'ㅅ')] = 'ㄳ', [('ㄴ', 'ㅈ')] = 'ㄵ', [('ㄴ', 'ㅎ')] = 'ㄶ', [('ㄹ', 'ㄱ')] = 'ㄺ',
        [('ㄹ', 'ㅁ')] = 'ㄻ', [('ㄹ', 'ㅂ')] = 'ㄼ', [('ㄹ', 'ㅅ')] = 'ㄽ', [('ㄹ', 'ㅌ')] = 'ㄾ',
        [('ㄹ', 'ㅍ')] = 'ㄿ', [('ㄹ', 'ㅎ')] = 'ㅀ', [('ㅂ', 'ㅅ')] = 'ㅄ'
    };

    private static readonly Dictionary<char, (char First, char Second)> Splits =
        Pairs.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static bool IsJamo(char ch)
    {
        return ch is >= 'ㄱ' and <= LastVowel;
    }

    public static bool IsSyllable(char ch)
    {
        return ch is >= FirstSyllable and <= LastSyllable;
    }

    private static bool IsVowel(char ch)
    {
        return ch is >= FirstVowel and <= LastVowel;
    }

    // Every syllable, and every letter two keys make, as the keys that type it.
    public static string Decompose(string text)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!IsSyllable(ch) && !Splits.ContainsKey(ch))
            {
                sb?.Append(ch);
                continue;
            }

            sb ??= new StringBuilder(text, 0, i, text.Length * 3);
            if (!IsSyllable(ch))
            {
                AppendKeys(sb, ch);
                continue;
            }

            var index = ch - FirstSyllable;
            sb.Append(Initials[index / (VowelCount * FinalCount)]);
            AppendKeys(sb, (char)(FirstVowel + index / FinalCount % VowelCount));
            if (index % FinalCount is var final and > 0)
                AppendKeys(sb, Finals[final]);
        }

        return sb?.ToString() ?? text;
    }

    private static void AppendKeys(StringBuilder sb, char jamo)
    {
        if (Splits.TryGetValue(jamo, out var keys))
            sb.Append(keys.First).Append(keys.Second);
        else
            sb.Append(jamo);
    }

    public static string Compose(string keys)
    {
        var at = 0;
        while (at < keys.Length && !IsJamo(keys[at]))
            at++;
        if (at == keys.Length)
            return keys;

        var sb = new StringBuilder(keys, 0, at, keys.Length);
        var block = new Block();
        for (; at < keys.Length; at++)
        {
            var ch = keys[at];
            if (!IsJamo(ch))
            {
                block.Flush(sb);
                sb.Append(ch);
            }
            else if (IsVowel(ch))
            {
                block.AddVowel(ch, sb);
            }
            else
            {
                block.AddConsonant(ch, sb);
            }
        }

        block.Flush(sb);
        return sb.ToString();
    }

    // The syllable being typed: any of an initial, a vowel and a final, '\0' where there is none yet.
    private struct Block
    {
        private char _initial;
        private char _vowel;
        private char _final;

        public void AddConsonant(char ch, StringBuilder sb)
        {
            if (_vowel == '\0' || _initial == '\0')
            {
                Flush(sb);
                _initial = ch;
            }
            else if (_final == '\0' && Finals.Contains(ch))
            {
                _final = ch;
            }
            else if (_final != '\0' && Pairs.TryGetValue((_final, ch), out var pair))
            {
                _final = pair;
            }
            else
            {
                Flush(sb);
                _initial = ch;
            }
        }

        public void AddVowel(char ch, StringBuilder sb)
        {
            if (_vowel == '\0')
            {
                _vowel = ch;
            }
            else if (_final == '\0' && Pairs.TryGetValue((_vowel, ch), out var pair))
            {
                _vowel = pair;
            }
            else if (_final == '\0')
            {
                Flush(sb);
                _vowel = ch;
            }
            else
            {
                // The final was the next syllable's initial all along: 갑 + ㅣ is 가비, 값 + ㅣ is 갑시.
                var moved = _final;
                _final = '\0';
                if (Splits.TryGetValue(moved, out var keys))
                    (_final, moved) = keys;

                Flush(sb);
                _initial = moved;
                _vowel = ch;
            }
        }

        public void Flush(StringBuilder sb)
        {
            if (_initial != '\0' && _vowel != '\0')
                sb.Append((char)(FirstSyllable +
                                 (Initials.IndexOf(_initial) * VowelCount + (_vowel - FirstVowel)) * FinalCount +
                                 (_final == '\0' ? 0 : Finals.IndexOf(_final))));
            else if (_initial != '\0')
                sb.Append(_initial);
            else if (_vowel != '\0')
                sb.Append(_vowel);

            this = default;
        }
    }
}
