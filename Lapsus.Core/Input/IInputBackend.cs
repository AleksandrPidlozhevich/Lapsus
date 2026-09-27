using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Text;

namespace Lapsus.Core.Input;

public interface IInputBackend : IDisposable
{
    bool IsRunning { get; }

    int HotkeyVirtualKey { get; set; }

    void SetSelectionHotkey(SelectionAction action, int trigger);

    bool AutoMode { get; set; }

    bool AutoFixTypos { get; set; }

    bool SwitchSystemLayout { get; set; }

    KeyboardLayout? PreferredLayout { get; set; }

    bool SkipPasswordFields { get; set; }

    void SetCorrector(IPhraseCorrector corrector);

    IReadOnlyList<LayoutCandidate> LayoutCandidates();

    void Start();

    void Stop();

    event EventHandler<CorrectionResult>? Corrected;

    event EventHandler<string>? Diagnostic;
}
