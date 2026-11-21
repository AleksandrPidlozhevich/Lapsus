using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.Input.Dictation;

// The live words of the part being said: each recognition of its audio so far is compared with the one before,
// and the words both agree on, from the start, are the ones to type (LocalAgreement-2). Typed words never change.
// A generation stops a recognition that finishes after its part was cut from being read as the next part's.
internal sealed class LiveWords
{
    private readonly List<string> _typed = [];
    private string[] _hypothesis = [];

    public int Generation { get; private set; }

    // The words to type now, or null; a recognition of an earlier generation, or one that reads the typed words
    // otherwise, adds nothing (the part's own recognition types the rest when the part ends).
    public string? Accept(string text, int generation)
    {
        if (generation != Generation)
            return null;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var agreed = Agreed(_hypothesis, words);
        _hypothesis = words;
        if (agreed <= _typed.Count || !words.Take(_typed.Count).SequenceEqual(_typed))
            return null;

        var fresh = words[_typed.Count..agreed];
        _typed.AddRange(fresh);
        return string.Join(' ', fresh);
    }

    // The live words of the audio just taken (its recognition must not type them again); the next part starts clean.
    public string[] Take()
    {
        var typed = _typed.ToArray();
        Reset(typed: []);
        return typed;
    }

    // An early part dropped because speech resumed gives its audio back, and with it its live words.
    public void Restore(string[] typed) => Reset(typed);

    private void Reset(string[] typed)
    {
        Generation++;
        _typed.Clear();
        _typed.AddRange(typed);
        _hypothesis = [];
    }

    // How many words, from the start, two recognitions agree on. The latest one's last word may be cut mid-way
    // (or carry the full stop of an unfinished sentence), so it never counts.
    internal static int Agreed(string[] previous, string[] latest)
    {
        var max = Math.Min(previous.Length, latest.Length - 1);
        var n = 0;
        while (n < max && previous[n] == latest[n])
            n++;
        return n;
    }

    // What a part's own recognition adds after its live words.
    // shortcut: found by word count, so a final reading that splits or joins a typed word drops or repeats one;
    // matching by content would be the upgrade if live tests show it.
    public static string After(string text, string[] typed) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(typed.Length));
}
