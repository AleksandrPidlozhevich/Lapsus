namespace Lapsus.Startup;

internal sealed class NullStartupRegistration : IStartupRegistration
{
    public bool IsSupported => false;

    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }
}
