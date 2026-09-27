using System.Diagnostics;
using Lapsus.Licensing;
using Xunit.Abstractions;

// xUnit1031 wants await; that hops threads and breaks Dispatcher.UIThread in later tests.
#pragma warning disable xUnit1031

namespace Lapsus.Tests;

public class ManagedDeviceProbeTests(ITestOutputHelper output)
{
    [Fact]
    public void Answers_quickly_and_without_throwing()
    {
        var watch = Stopwatch.StartNew();
        var ownership = ManagedDeviceProbe.DetectAsync().GetAwaiter().GetResult();
        watch.Stop();

        output.WriteLine($"{ownership} in {watch.ElapsedMilliseconds} ms");

        Assert.True(Enum.IsDefined(ownership));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"probe took {watch.Elapsed}");
    }

    [Fact]
    public void Detects_once_and_reuses_the_answer()
    {
        var first = ManagedDeviceProbe.DetectAsync().GetAwaiter().GetResult();

        var watch = Stopwatch.StartNew();
        var second = ManagedDeviceProbe.DetectAsync().GetAwaiter().GetResult();
        watch.Stop();

        Assert.Equal(first, second);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(50), $"second call took {watch.Elapsed}");
    }
}
