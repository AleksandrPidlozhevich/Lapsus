using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input.Dictation;

// Languages: the others detection may pick.
// PreviousText: what precedes the caret (typed, or this field's last dictation), so the next one continues it.
internal sealed record DictationRequest(string Language, IReadOnlyList<string> Languages, string? PreviousText = null);

// Language: the one the text was recognised in, so the rest of the dictation can skip detection.
internal sealed record DictationResult(string Text, string Language);

internal interface IDictationEngine
{
    // Whether the speech model is on disk; without it a dictation does not start.
    bool HasModel { get; }

    // Starts loading the model now, while the user speaks, so it is ready when the recording ends.
    void Prepare();

    Task<DictationResult> TranscribeAsync(float[] pcm, DictationRequest request, CancellationToken cancellationToken);
}
