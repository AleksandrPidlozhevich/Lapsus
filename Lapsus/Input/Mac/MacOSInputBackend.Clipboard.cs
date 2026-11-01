using Avalonia.Threading;
using Lapsus.Core.Correction;
using Lapsus.Core.Input;
using Lapsus.Core.Layout;
using Lapsus.Localization;
using Lapsus.Startup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input;

// Selection actions through the pasteboard: copy, paste, restore.
internal sealed partial class MacOSInputBackend
{
    private const int CopyTimeoutMs = 500;

    private const int CopyPollMs = 10;

    private const int ReadAttempts = 3;

    private const int RestoreDelayMs = 500;

    private MacClipboard.Snapshot? _savedClipboard;

    private bool _clipboardDirty;

    private int _clipboardEpoch;

    protected override async Task<string?> CopySelectionAsync()
    {
        // A correction may already be holding the user's clipboard for a delayed restore.
        _clipboardEpoch++;
        if (!_clipboardDirty)
            _savedClipboard = MacClipboard.CaptureSnapshot();

        MacClipboard.WasModified();
        if (!MacTextInjection.SendCommandChord(MacOSNativeMethods.CKeyCode, MacFrontmost.ProcessId()))
            return null;

        for (var waited = 0; waited < CopyTimeoutMs; waited += CopyPollMs)
        {
            await Task.Delay(CopyPollMs).ConfigureAwait(true);
            if (!MacClipboard.WasModified())
                continue;

            _clipboardDirty = true;

            for (var attempt = 0; attempt < ReadAttempts; attempt++)
            {
                if (MacClipboard.GetText() is { Length: > 0 } copied)
                    return copied;

                await Task.Delay(CopyPollMs).ConfigureAwait(true);
            }

            return null;
        }

        return null;
    }

    protected override bool PasteText(string text)
    {
        return PasteInto(MacFrontmost.ProcessId(), text);
    }

    private bool PasteInto(int pid, string text, bool onSession = false)
    {
        if (!MacClipboard.SetText(text))
        {
            RaiseDiagnosticFormat("Diag_ClipboardFailed", ForegroundAppName());
            return false;
        }

        // Swallow our own write so that only a later, foreign change counts when restoring.
        MacClipboard.WasModified();
        _clipboardDirty = true;
        _clipboardEpoch++;
        var pasted = onSession
            ? MacTextInjection.SendSessionCommandChord(MacOSNativeMethods.VKeyCode)
            : MacTextInjection.SendCommandChord(MacOSNativeMethods.VKeyCode, pid);
        if (pasted)
            return true;

        RaiseDiagnosticFormat("Diag_ClipboardFailed", ForegroundAppName());
        return false;
    }

    protected override async Task RestoreClipboardAsync()
    {
        if (!_clipboardDirty)
            return;

        var epoch = _clipboardEpoch;
        var saved = _savedClipboard;
        await Task.Delay(RestoreDelayMs).ConfigureAwait(true);

        // A newer correction or a selection copy took over the clipboard.
        if (epoch != _clipboardEpoch || !_clipboardDirty)
            return;

        _clipboardDirty = false;
        _savedClipboard = null;

        // The user copied something while we waited; that copy is newer than our snapshot.
        if (MacClipboard.WasModified())
        {
            CaptureLog.Write("Clipboard changed during a correction; the newer copy is kept.");
            return;
        }

        MacClipboard.RestoreSnapshot(saved);
        MacClipboard.WasModified();
    }
}
