namespace Lapsus.Core.Models;

// The languages each catalog model reads well enough to choose among the spellings of a slip, as measured
// with tools/Lapsus.Sweep --neural (natural sentences with one typo, against the dictionary alone). In any
// other language the dictionary's spelling stands; the model still judges layouts there. A model missing
// from the table has not been measured and chooses in every language.
public static class SpellingLanguages
{
    private static readonly Dictionary<string, string[]> Measured = new(StringComparer.OrdinalIgnoreCase)
    {
        // el, bg, be and ka lost 2–6 pp to the dictionary.
        ["qwen3-0.6b-instruct-int4"] = ["en", "ru", "uk", "he", "ar"]
    };

    public static IReadOnlySet<string>? For(string? modelId)
    {
        return modelId is not null && Measured.TryGetValue(modelId, out var languages)
            ? new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase)
            : null;
    }
}
