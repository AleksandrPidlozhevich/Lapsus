using Lapsus.Core.Layout;

namespace Lapsus.Core.Correction;

public sealed class WordExceptions
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _words = new(StringComparer.Ordinal);

    public event EventHandler? Changed;

    public WordExceptions()
    {
    }

    public WordExceptions(IEnumerable<string> words)
    {
        foreach (var word in words)
            if (Normalize(word) is { } normalized)
                _words.Add(normalized);
    }

    public bool Contains(string word)
    {
        if (Normalize(word) is not { } normalized)
            return false;

        lock (_gate)
        {
            return _words.Contains(normalized);
        }
    }

    public bool Add(string word)
    {
        if (Normalize(word) is not { } normalized)
            return false;

        lock (_gate)
        {
            if (!_words.Add(normalized))
                return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(string word)
    {
        if (Normalize(word) is not { } normalized)
            return false;

        lock (_gate)
        {
            if (!_words.Remove(normalized))
                return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _words.Count == 0;
            }
        }
    }

    internal List<TextSpan>? ChunkSpans(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        List<TextSpan>? spans = null;

        lock (_gate)
        {
            if (_words.Count == 0)
                return null;

            var i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;

                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;

                if (i > start && Normalize(text[start..i]) is { } normalized && _words.Contains(normalized))
                    (spans ??= []).Add(new TextSpan(start, i - start, true));
            }
        }

        return spans;
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            var all = new List<string>(_words);
            all.Sort(StringComparer.Ordinal);
            return all;
        }
    }

    private static string? Normalize(string word)
    {
        var start = 0;
        var end = word.Length;
        while (start < end && !char.IsLetter(word[start])) start++;
        while (end > start && !char.IsLetter(word[end - 1])) end--;

        return end > start ? word[start..end].ToLowerInvariant() : null;
    }
}
