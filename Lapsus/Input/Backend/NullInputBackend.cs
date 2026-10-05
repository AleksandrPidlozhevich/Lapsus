using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Core.Text;
using System;
using System.Collections.Generic;

namespace Lapsus.Input;

public sealed class NullInputBackend : IInputBackend
{
    public bool IsRunning => false;
    public int HotkeyVirtualKey { get; set; }

    public void SetSelectionHotkey(SelectionAction action, int trigger)
    {
    }

    public bool AutoMode { get; set; }

    public bool AutoFixTypos { get; set; }
    public bool SwitchSystemLayout { get; set; }
    public KeyboardLayout? PreferredLayout { get; set; }

    public bool SkipPasswordFields { get; set; } = true;

#pragma warning disable CS0067
    public event EventHandler<CorrectionResult>? Corrected;
    public event EventHandler<string>? Diagnostic;
#pragma warning restore CS0067

    public void SetCorrector(IPhraseCorrector corrector)
    {
    }

    public IReadOnlyList<LayoutCandidate> LayoutCandidates()
    {
        return [];
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
