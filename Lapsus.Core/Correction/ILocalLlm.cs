namespace Lapsus.Core.Correction;

public interface ILocalLlm : IDisposable
{
    bool IsLoaded { get; }

    void Unload();

    Task<string> CompleteAsync(
        string systemPrompt, string userText, int answerChars, CancellationToken cancellationToken = default);
}
