using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Startup;

// Sandboxed (Mac App Store) autostart: the app registers itself as a login item through
// SMAppService (macOS 13+). The sandbox cannot write ~/Library/LaunchAgents or run launchctl, which
// is what MacOsStartupRegistration does for the direct download. The user can switch it off in
// System Settings > General > Login Items, so IsEnabled asks the system rather than our settings.
[SupportedOSPlatform("macos13.0")]
internal sealed class MacOsSandboxStartupRegistration : IStartupRegistration
{
    private const string ServiceManagement =
        "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

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

        var selector = sel_registerName(enabled ? "registerAndReturnError:" : "unregisterAndReturnError:");
        if (objc_msgSend_error(service, selector, out var error))
            return;

        throw new InvalidOperationException(Describe(error));
    }

    private static long Status()
    {
        var service = MainAppService();
        return service == IntPtr.Zero ? 0 : objc_msgSend_long(service, sel_registerName("status"));
    }

    private static IntPtr MainAppService()
    {
        if (!FrameworkLoaded)
            return IntPtr.Zero;

        var type = objc_getClass("SMAppService");
        return type == IntPtr.Zero ? IntPtr.Zero : objc_msgSend_IntPtr(type, sel_registerName("mainAppService"));
    }

    private static string Describe(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return "SMAppService failed.";

        var description = objc_msgSend_IntPtr(error, sel_registerName("localizedDescription"));
        var utf8 = description == IntPtr.Zero
            ? IntPtr.Zero
            : objc_msgSend_IntPtr(description, sel_registerName("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8) ?? "SMAppService failed.";
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_error(IntPtr receiver, IntPtr selector, out IntPtr error);
}
