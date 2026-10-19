using Lapsus.Startup;

namespace Lapsus.Tests;

public class MacSandboxTests
{
    [Fact]
    public void A_mac_process_with_a_container_is_sandboxed()
    {
        Assert.True(MacSandbox.Detect(isMacOS: true, containerId: "com.lapsus.app"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_mac_process_without_a_container_is_not(string? containerId)
    {
        Assert.False(MacSandbox.Detect(isMacOS: true, containerId));
    }

    // The variable means nothing elsewhere; a stray one must not switch a Windows build to store behavior.
    [Fact]
    public void The_variable_is_ignored_off_macOS()
    {
        Assert.False(MacSandbox.Detect(isMacOS: false, containerId: "com.lapsus.app"));
    }

    // dotnet test is not sandboxed, so every store-only branch stays off for the rest of the suite.
    [Fact]
    public void The_test_run_itself_is_not_sandboxed()
    {
        Assert.False(MacSandbox.IsSandboxed);
    }
}
