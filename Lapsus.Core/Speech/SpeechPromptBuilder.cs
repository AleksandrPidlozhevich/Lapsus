using System.Text;

namespace Lapsus.Core.Speech;

// The text Whisper reads as "what came before" the audio. It decides spelling more than anything else: shown
// a sentence that keeps English terms in Latin, the decoder keeps them too (stage 1: terms written as-is went
// from 2–5 of 25 to 13–16 of 25). Three parts, nearest to the audio last because that is what weighs most:
// a sample sentence in the language, the user's glossary, and the tail of what this session already dictated.
public static class SpeechPromptBuilder
{
    // Whisper takes at most 224 prompt tokens; non-Latin scripts cost about a token per one or two characters.
    public const int MaxChars = 360;

    private const int MaxPreviousChars = 120;

    // Sentences that mix the language with English terms the way people speak. Written by Claude, not checked
    // by native speakers yet (except ru).
    private static readonly Dictionary<string, string> Samples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ru"] = "Закинул pull request в GitHub, после code review задеплою на staging и обновлю README.",
        ["uk"] = "Закинув pull request у GitHub, після code review задеплою на staging і оновлю README.",
        ["be"] = "Закінуў pull request у GitHub, пасля code review задэплою на staging і абнаўлю README.",
        ["bg"] = "Пуснах pull request в GitHub, след code review ще го пусна на staging и ще обновя README.",
        ["mk"] = "Пуштив pull request на GitHub, по code review ќе го пуштам на staging и ќе го ажурирам README.",
        ["sr"] = "Пустио сам pull request на GitHub, после code review ћу то пустити на staging.",
        ["el"] = "Άνοιξα ένα pull request στο GitHub και μετά το code review θα κάνω deploy στο staging.",
        ["he"] = "פתחתי pull request ב-GitHub, ואחרי ה-code review אעשה deploy ל-staging ואעדכן את ה-README.",
        ["ar"] = "فتحت pull request على GitHub، وبعد الـ code review سأعمل deploy على staging وأحدّث README.",
        ["ka"] = "GitHub-ზე pull request გავხსენი, code review-ს შემდეგ staging-ზე deploy გავაკეთებ.",
        ["en"] = "Opened a pull request on GitHub; after code review I'll deploy to staging and update the README.",
    };

    public static string? SampleFor(string language) =>
        Samples.GetValueOrDefault(DictationLanguagePicker.Normalize(language));

    public static string Build(string language, IEnumerable<string>? glossary = null, string? previousText = null)
    {
        var sample = SampleFor(language) ?? "";
        var previous = Tail(previousText, MaxPreviousChars);

        var room = MaxChars - sample.Length - previous.Length - 2;
        var terms = new StringBuilder();
        foreach (var raw in glossary ?? [])
        {
            var term = raw.Trim();
            if (term.Length == 0 || terms.ToString().Contains(term, StringComparison.Ordinal))
                continue;
            if (terms.Length + term.Length + 2 > room)
                break;
            if (terms.Length > 0)
                terms.Append(", ");
            terms.Append(term);
        }
        if (terms.Length > 0)
            terms.Append('.');

        return string.Join(' ', new[] { sample, terms.ToString(), previous }.Where(p => p.Length > 0));
    }

    // The last few words of earlier text, cut at a word boundary.
    private static string Tail(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
            return trimmed;

        var cut = trimmed[^maxChars..];
        var space = cut.IndexOf(' ');
        return space >= 0 && space < cut.Length - 1 ? cut[(space + 1)..] : cut;
    }
}
