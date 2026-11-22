using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input.Dictation;

// Languages: the others detection may pick.
// PreviousText: what precedes the caret (typed, or this field's last dictation), so the next one continues it.
// Live: a recognition for live words, run every few hundred ms — kept out of the capture log.
internal sealed record DictationRequest(
    string Language, IReadOnlyList<string> Languages, string? PreviousText = null, bool Live = false);

// Language: the one the text was recognised in, so the rest of the dictation can skip detection.
internal sealed record DictationResult(string Text, string Language);

internal interface IDictationEngine
{
    // Whether the speech model is on disk; without it a dictation does not start.
    bool HasModel { get; }

    // Starts loading the model now, while the user speaks, so it is ready when the recording ends. Language: the
    // active layout's, which picks among the downloaded models; null for the chosen one.
    void Prepare(string? language = null);

    // Whether recognition is fast enough (a fraction of a second) to run again and again while the user speaks.
    bool CanStream { get; }

    // Starts loading the model (see Prepare) and keeps it loaded until the hold is disposed, so an idle unload
    // never lands between listening and the last part.
    IDisposable Hold(string? language = null);

    Task<DictationResult> TranscribeAsync(float[] pcm, DictationRequest request, CancellationToken cancellationToken);
}
