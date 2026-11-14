using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using Lapsus.Core.Text;
using Lapsus.Input;

namespace Lapsus.Tests;

// A selection is read from the clipboard, so it is fixed the same way whichever keyboard is on.
public sealed class KoreanSelectionTests : IDisposable
{
    private readonly string _enPath = WriteTemp("en", "hello 900", "windows 800", "world 700");
    private readonly string _koPath = WriteTemp("ko", "안녕 900", "있어요 800", "값이 700");

    public void Dispose()
    {
        File.Delete(_enPath);
        File.Delete(_koPath);
    }

    [Theory]
    [InlineData("dkssud", "안녕")]
    [InlineData("rkqtdl dlTdjdy", "값이 있어요")]
    [InlineData("ㅗ디ㅣㅐ", "hello")]
    [InlineData("ㅗ디ㅣㅐ 재깅", "hello world")]
    public void A_selection_typed_on_the_other_keyboard_is_put_right(string selected, string expected)
    {
        foreach (var active in new[] { FakeInputBackend.En, FakeInputBackend.Ko })
        {
            var backend = Backend(active);
            backend.Selection = selected;

            Correct(backend);

            Assert.Equal(expected, Assert.Single(backend.Pasted));
        }
    }

    [Theory]
    [InlineData("안녕")]
    [InlineData("hello world")]
    public void A_selection_that_reads_as_it_stands_is_not_touched(string selected)
    {
        var backend = Backend(FakeInputBackend.En);
        backend.Selection = selected;

        Correct(backend);

        Assert.Empty(backend.Pasted);
    }

    // Waited for on this thread, never awaited: the correction runs on the pool, and a test that came back on
    // a pool thread would leave the UI dispatcher every later test shares belonging to the wrong one.
    private static void Correct(FakeInputBackend backend)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        backend.RunSelectionAction(SelectionAction.Correct).GetAwaiter().GetResult();
    }

    private FakeInputBackend Backend(InstalledLayout active)
    {
        var brain = new LayoutCorrector(new SpellChecker(
        [
            new DictionarySource("en", _enPath, Script.Latin),
            new DictionarySource("ko", _koPath, Script.Hangul)
        ]));

        return new FakeInputBackend(brain)
        {
            InstalledLayouts = [FakeInputBackend.En, FakeInputBackend.Ko],
            Active = active
        }.Listening().Focused();
    }

    private static string WriteTemp(string tag, params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{tag}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}
