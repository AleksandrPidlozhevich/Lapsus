using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Tests;

public sealed class BrainFailureTests
{
    [Fact]
    public void A_throwing_brain_reports_instead_of_escaping_the_hotkey_path()
    {
        var backend = new FakeInputBackend(new ThrowingCorrector()).Listening();
        backend.Type("ghbdtn");

        backend.PressHotkey();

        Assert.Contains(backend.Diagnostics, d => d.Contains("brain gave up", StringComparison.Ordinal));
        Assert.Empty(backend.Injected);
    }

    [Fact]
    public void A_throwing_brain_reports_instead_of_escaping_the_auto_path()
    {
        var backend = new FakeInputBackend(new ThrowingCorrector()).Listening().Focused();
        backend.AutoMode = true;

        backend.Type("ghbdtn ");
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(backend.Diagnostics, d => d.Contains("brain gave up", StringComparison.Ordinal));
        Assert.Empty(backend.Injected);
    }

    private sealed class ThrowingCorrector : IPhraseCorrector
    {
        public bool IsReady => true;

        public bool SupportsLayoutCycle => false;

        public bool PreferAsync => false;

        public bool SupportsAutoMode => true;

        public PhraseCorrection CorrectPhrase(
            string text,
            LayoutSource active,
            IReadOnlyList<LayoutSource> installed,
            IReadOnlyList<LayoutCandidate> candidates,
            KeyboardLayout? preferred = null)
        {
            throw new InvalidOperationException("brain gave up");
        }
    }
}
