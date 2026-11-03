using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using Lapsus.Input;

namespace Lapsus.Tests;

// "…специалист по ГЧ": UX typed on the Russian keys at the end of a Russian line. The acronym is no slip for
// the letter "ч", and the short "по" beside it stays Russian like the rest of the line.
public sealed class AcronymOnWrongKeysCycleTests : IDisposable
{
    private readonly List<string> _paths = [];

    [Fact]
    public void First_press_switches_only_the_acronym()
    {
        var ru = new InstalledLayout("ru-id", 2, Script.Cyrillic, "ru", KeyboardLayout.Ru, BundledKeyboardMaps.Ru);
        var en = new InstalledLayout("en-id", 1, Script.Latin, "en", KeyboardLayout.En, BundledKeyboardMaps.En);
        var brain = new LayoutCorrector(new SpellChecker([
            new DictionarySource("ru", List("ru", "и 2917112", "как 1174339", "по 397721", "ч 7175",
                "внимательно 4677", "специалист 1836", "великолепный 1461", "дизайнер 608", "пройдись 213"),
                Script.Cyrillic),
            new DictionarySource("en", List("en", "the 9000000", "up 2459855", "design 90000", "user 80000"),
                Script.Latin)
        ]));

        var cycle = LayoutCorrectionCycle.Start(brain,
            "пройдись внимательно как великолепный дизайнер и специалист по ГЧ", ru, [en, ru], null);

        Assert.NotNull(cycle);
        Assert.Equal("пройдись внимательно как великолепный дизайнер и специалист по UX", cycle.Current.Text);
    }

    // The toast "…по Ч/ → ghjlbcm внимательно как dtkbrjktgysq … UX": the brain left two listed words out of
    // the ones it vouched for, and the next press flipped them. A word the dictionary knows stays as typed.
    [Fact]
    public void Next_press_never_flips_a_word_the_dictionary_knows()
    {
        var ru = new InstalledLayout("ru-id", 2, Script.Cyrillic, "ru", KeyboardLayout.Ru, BundledKeyboardMaps.Ru);
        var en = new InstalledLayout("en-id", 1, Script.Latin, "en", KeyboardLayout.En, BundledKeyboardMaps.En);
        const string typed = "пройдись внимательно как великолепный дизайнер и специалист по ГЧ";
        var brain = new ForgetfulBrain(typed);

        var cycle = LayoutCorrectionCycle.Start(brain, typed, ru, [en, ru], null);

        Assert.NotNull(cycle);
        Assert.Equal("пройдись внимательно как великолепный дизайнер и специалист по Ч", cycle.Current.Text);
        Assert.Equal("пройдись внимательно как великолепный дизайнер и специалист по UX", cycle.MoveNext().Text);
    }

    private sealed class ForgetfulBrain(string typed) : IPhraseCorrector
    {
        private static readonly HashSet<string> Known =
            ["пройдись", "внимательно", "как", "великолепный", "дизайнер", "и", "специалист", "по", "ч"];

        public bool IsReady => true;

        public bool SupportsLayoutCycle => true;

        public bool PreferAsync => false;

        public bool Knows(Script script) => script == Script.Cyrillic;

        public bool KnowsWord(string word, Script script) => Known.Contains(word.ToLowerInvariant());

        public PhraseCorrection CorrectPhrase(string text, LayoutSource active, IReadOnlyList<LayoutSource> installed,
            IReadOnlyList<LayoutCandidate> candidates, KeyboardLayout? preferred = null)
        {
            // Vouches for every word but пройдись and великолепный.
            TextSpan Span(string word) => new(typed.IndexOf(word, StringComparison.Ordinal), word.Length);
            return new PhraseCorrection(text, text.Replace("ГЧ", "Ч"), true, null, null,
                [Span("внимательно"), Span("как"), Span("дизайнер"), Span("специалист")], [Span("по")]);
        }
    }

    public void Dispose()
    {
        foreach (var path in _paths)
            File.Delete(path);
    }

    private string List(string code, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-acronym-{code}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        _paths.Add(path);
        return path;
    }
}
