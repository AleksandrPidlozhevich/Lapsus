using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lapsus.Input.Dictation;

// Language: the one to dictate in (the active layout's); Languages: the others detection may pick;
// PreviousText: what this field got from the last dictation, so the next one continues it.
internal sealed record DictationRequest(string Language, IReadOnlyList<string> Languages, string? PreviousText = null);

// Speech to finished text: recognition, cleanup and English terms. DictationEngine is the real one.
internal interface IDictationEngine
{
    // Whether the speech model is on disk; without it a dictation does not start.
    bool HasModel { get; }

    // Starts loading the model now, while the user speaks, so it is ready when the recording ends.
    void Prepare();

    Task<string> TranscribeAsync(float[] pcm, DictationRequest request, CancellationToken cancellationToken);
}
