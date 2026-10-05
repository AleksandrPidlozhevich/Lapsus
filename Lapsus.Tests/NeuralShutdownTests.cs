using Lapsus.Neural;

namespace Lapsus.Tests;

public class NeuralShutdownTests
{
    [Fact]
    public async Task Abandoning_refuses_work_that_arrives_after_the_user_has_quit()
    {
        var llm = new OnnxGenAiLlm();
        llm.AbandonForShutdown();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => llm.CompleteAsync("system", "ghbdtn", 6));
        Assert.False(llm.IsLoaded);
    }

    [Fact]
    public void Abandoning_twice_and_disposing_after_are_all_no_ops()
    {
        var llm = new OnnxGenAiLlm();

        llm.AbandonForShutdown();
        llm.AbandonForShutdown();
        llm.Dispose();
    }
}
