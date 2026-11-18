using Lapsus.Input;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Startup;

// The sandbox cannot write ~/Library/LaunchAgents or run launchctl.
// The user can switch the login item off in System Settings, so IsEnabled asks the system rather than our settings.
[SupportedOSPlatform("macos13.0")]
internal sealed class MacOsSandboxStartupRegistration : IStartupRegistration
{
    private const string ServiceManagement =
        "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";

    private const long StatusEnabled = 1;
    private const long StatusRequiresApproval = 2;

    private static readonly bool FrameworkLoaded = NativeLibrary.TryLoad(ServiceManagement, out _);

    public bool IsSupported => FrameworkLoaded && MainAppService() != IntPtr.Zero;

    // Requires approval counts as on: the item is registered and the user has been asked.
    public bool IsEnabled => Status() is StatusEnabled or StatusRequiresApproval;

    public void SetEnabled(bool enabled)
    {
        var service = MainAppService();
        if (service == IntPtr.Zero || IsEnabled == enabled)
            return;

        var selector = ObjC.Selector(enabled ? "registerAndReturnError:" : "unregisterAndReturnError:");
        if (ObjC.SendReturningBool(service, selector, out var error))
            return;

        throw new InvalidOperationException(Describe(error));
    }

    private static long Status()
    {
        var service = MainAppService();
        return service == IntPtr.Zero ? 0 : (long)ObjC.Send(service, ObjC.Selector("status"));
    }

    private static IntPtr MainAppService()
    {
        if (!FrameworkLoaded)
            return IntPtr.Zero;

        var type = ObjC.Class("SMAppService");
        return type == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(type, ObjC.Selector("mainAppService"));
    }

    private static string Describe(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return "SMAppService failed.";

        var description = ObjC.Send(error, ObjC.Selector("localizedDescription"));
        var utf8 = description == IntPtr.Zero
            ? IntPtr.Zero
            : ObjC.Send(description, ObjC.Selector("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8) ?? "SMAppService failed.";
    }
}
