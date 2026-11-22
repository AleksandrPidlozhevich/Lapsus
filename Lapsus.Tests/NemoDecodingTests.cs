using Lapsus.Speech;

namespace Lapsus.Tests;

public sealed class NemoDecodingTests
{
    private const int Blank = 9;

    [Fact]
    public void TokensAreKeptJumpsSkipFramesAndStateAdvancesOnlyOnATokenKept()
    {
        // frame 0: token 1 with no jump, then blank; frame 1: token 2 jumping 2 frames; frame 3: blank, end.
        var script = new Queue<(int, int)>([(1, 0), (Blank, 0), (2, 2), (Blank, 1)]);
        var seen = new List<(int Frame, int Last)>();
        var accepted = 0;

        var tokens = NemoModel.DecodeTdt(4, Blank, (t, last) =>
        {
            seen.Add((t, last));
            return script.Dequeue();
        }, () => accepted++);

        Assert.Equal([1, 2], tokens);
        Assert.Equal([(0, Blank), (0, 1), (1, 1), (3, 2)], seen);
        Assert.Equal(2, accepted);
    }

    [Fact]
    public void AFrameThatNeverStopsEmittingMovesOnAfterTenTokens()
    {
        var tokens = NemoModel.DecodeTdt(2, Blank, (_, _) => (5, 0), () => { });

        Assert.Equal(20, tokens.Count);
    }

    [Fact]
    public void WordStartsBecomeSpacesButNotBeforePunctuation()
    {
        var text = NemoModel.Detokenize(["▁При", "вет", "▁", ",", "▁мир", "▁", "."]);

        Assert.Equal("Привет, мир.", text);
    }

    [Fact]
    public void CtcKeepsATokenWhereItChangesAndIsNotBlank()
    {
        // A repeat is one token unless a blank splits it.
        Assert.Equal([1, 1, 2, 2], NemoModel.DecodeCtc([1, 1, Blank, 1, 2, Blank, 2, 2], Blank));
    }

    [Theory]
    [InlineData("Ես եւ դու, Եւ երթեւեկություն", "hy", "Ես և դու, Եվ երթևեկություն")]
    [InlineData("Ես եւ դու", "ru", "Ես եւ դու")]
    public void ArmenianTakesTheLigature(string text, string language, string expected)
    {
        Assert.Equal(expected, NemoRecognizer.Orthography(text, language));
    }

    [Theory]
    [InlineData(new[] { 0.9f, 0.9f, 0.1f, 0.9f, 0.9f }, false)]
    [InlineData(new[] { 0.1f, 0.6f, 0.7f, 0.8f, 0.1f }, true)]
    [InlineData(new float[0], false)]
    public void SpeechIsAFewHopsInARowNotOneClick(float[] probabilities, bool speech)
    {
        Assert.Equal(speech, SileroVad.Speaks(probabilities));
    }
}
