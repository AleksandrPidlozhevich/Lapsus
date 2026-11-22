using System;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// Hardware key events report the event source, which is not the app being typed into, so
// Chromium detection cannot use that pid.
[SupportedOSPlatform("macos")]
internal static class MacFrontmost
{
    private static readonly IntPtr Workspace = ObjC.Class("NSWorkspace");
    private static readonly IntPtr SharedWorkspace = ObjC.Selector("sharedWorkspace");
    private static readonly IntPtr FrontmostApplication = ObjC.Selector("frontmostApplication");
    private static readonly IntPtr ProcessIdentifier = ObjC.Selector("processIdentifier");

    public static int ProcessId()
    {
        // The caret sampler asks from a pool thread, which has no autorelease pool of its own.
        var pool = ObjC.PushPool();
        try
        {
            var workspace = ObjC.Send(Workspace, SharedWorkspace);
            if (workspace == IntPtr.Zero)
                return 0;

            var app = ObjC.Send(workspace, FrontmostApplication);
            if (app == IntPtr.Zero)
                return 0;

            var pid = ObjC.SendReturningInt(app, ProcessIdentifier);
            return pid > 0 ? pid : 0;
        }
        finally
        {
            ObjC.PopPool(pool);
        }
    }
}
