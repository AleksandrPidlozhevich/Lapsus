using Lapsus.Input;
using System;
using System.Runtime.Versioning;

namespace Lapsus.Startup;

[SupportedOSPlatform("macos")]
internal static class MacOsDockVisibility
{
    private const long ActivationPolicyAccessory = 1;

    public static void HideFromDock()
    {
        var sharedApplication = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Selector("sharedApplication"));
        ObjC.Send(sharedApplication, ObjC.Selector("setActivationPolicy:"), (IntPtr)ActivationPolicyAccessory);
    }
}
