using System.Text;
using System.Text.Json;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Spelling;

public sealed class WordFormsBuilder(string languageCode, Script script)
{
    private readonly HashSet<string> _forms = new(StringComparer.Ordinal);

    public int Count => _forms.Count;

    public int AddWiktionary(IEnumerable<string> jsonLines)
    {
        var before = _forms.Count;
        foreach (var line in jsonLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var entry = JsonDocument.Parse(line);
            var root = entry.RootElement;
            if (root.TryGetProperty("word", out var word) && word.ValueKind == JsonValueKind.String)
                Keep(word.GetString());

            if (!root.TryGetProperty("forms", out var inflected) || inflected.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var form in inflected.EnumerateArray())
            {
                if (form.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array &&
                    tags.EnumerateArray().Any(t => t.GetString() is "romanization" or "class" or "table-tags" or "inflection-template"))
                    continue;

                if (form.TryGetProperty("form", out var spelled) && spelled.ValueKind == JsonValueKind.String)
                    Keep(spelled.GetString());
            }
        }

        return _forms.Count - before;
    }

    public int AddUniMorph(IEnumerable<string> lines)
    {
        var before = _forms.Count;
        foreach (var line in lines)
        {
            var fields = line.Split('\t');
            if (fields.Length >= 2)
                Keep(fields[1]);
        }

        return _forms.Count - before;
    }

    public void Write(string wordFormsPath)
    {
        var sorted = _forms.Order(StringComparer.Ordinal).ToList();
        var utf8 = new UTF8Encoding(false);
        File.WriteAllLines(wordFormsPath, [sorted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), .. sorted], utf8);
        File.WriteAllText(Path.ChangeExtension(wordFormsPath, ".aff"), "SET UTF-8\n", utf8);
    }

    private void Keep(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var word = Scripts.FoldMarks(raw.Normalize(NormalizationForm.FormC), script).TrimEnd('־', '-');
        if (Scripts.IsWordOf(word, script) && LanguageAlphabets.IsWordOf(word, script, languageCode))
            _forms.Add(word);
    }
}
