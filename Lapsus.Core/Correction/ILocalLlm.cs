namespace Lapsus.Core.Correction;

public interface ILocalLlm : IDisposable
{
    bool IsLoaded { get; }

    void Unload();

    // Log-probability of each text following the context, as the model reads plain text; with ends, also of
    // the text stopping there. Null when the runtime cannot score.
    Task<IReadOnlyList<double>?> ScoreAsync(
        string context, IReadOnlyList<string> texts, bool ends, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<double>?>(null);
    }
}
