using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests;

public class CharNgramModelTests
{
    private static CharNgramModel BuildRussian()
    {
        var words = SampleWords.Russian.Select((w, i) => (w, (long)(100000 / (i + 1))));
        return CharNgramModel.Build(words, Script.Cyrillic, minWords: 1)!;
    }

    [Fact]
    public void A_training_word_scores_high_and_its_keystrokes_on_the_wrong_layout_score_low()
    {
        var model = BuildRussian();

        Assert.True(model.Score("привет") > 0.8);
        Assert.True(model.Score("руддщ") < 0.2);
    }

    [Fact]
    public void An_unseen_word_of_the_language_still_reads_as_the_language()
    {
        var model = BuildRussian();

        Assert.True(model.Score("приветик") > 0.5);
        Assert.True(model.Score("приветик") > model.Score("gjhbdtn"));
    }

    [Fact]
    public void Case_does_not_matter()
    {
        var model = BuildRussian();

        Assert.Equal(model.Score("привет"), model.Score("Привет"));
    }

    [Fact]
    public void An_empty_word_scores_nothing()
    {
        var model = BuildRussian();

        Assert.Equal(0.0, model.Score(""));
    }

    [Fact]
    public void Too_small_a_list_teaches_no_model()
    {
        Assert.Null(CharNgramModel.Build([("привет", 10L), ("мир", 5L)], Script.Cyrillic));
    }

    [Fact]
    public void Words_with_characters_outside_the_script_are_skipped()
    {
        var words = new List<(string, long)> { ("co-op", 10L), ("don't", 5L), ("123", 4L) };
        Assert.Null(CharNgramModel.Build(words, Script.Latin, minWords: 1));
    }
}
