namespace Lapsus.Input.Dictation;

// The microphone badge at the caret. Called on the UI thread, except SetLevel, which may come from any thread.
internal interface IDictationIndicator
{
    void ShowListening();

    // Loudness 0..1 of what the microphone hears now.
    void SetLevel(float level);

    void ShowWorking();

    // A short message in place of the badge for a few seconds (no model, no microphone, nothing heard).
    void ShowMessage(string message);

    void Hide();
}
