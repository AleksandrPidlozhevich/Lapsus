using System;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// Spawning /usr/bin/open (what Process.Start with UseShellExecute does on macOS) is a child
// process, which the App Sandbox does not reliably allow.
[SupportedOSPlatform("macos")]
internal static class MacWorkspace
{
    public static bool OpenUrl(string url)
    {
        var pool = ObjC.PushPool();
        try
        {
            var text = ObjC.Send(ObjC.Class("NSString"), ObjC.Selector("stringWithUTF8String:"), url);
            var nsUrl = text == IntPtr.Zero
                ? IntPtr.Zero
                : ObjC.Send(ObjC.Class("NSURL"), ObjC.Selector("URLWithString:"), text);
            if (nsUrl == IntPtr.Zero)
                return false;

            var workspace = ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Selector("sharedWorkspace"));
            return workspace != IntPtr.Zero
                   && ObjC.SendReturningBool(workspace, ObjC.Selector("openURL:"), nsUrl);
        }
        finally
        {
            ObjC.PopPool(pool);
        }
    }
}
