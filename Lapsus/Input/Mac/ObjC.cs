using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lapsus.Input;

// objc_msgSend has no signature of its own: each overload states the C types of one call shape.
// Resolve classes and selectors once into static fields; both are process-lifetime constants.
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib, EntryPoint = "objc_getClass")]
    public static extern IntPtr Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, EntryPoint = "sel_registerName")]
    public static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, EntryPoint = "objc_autoreleasePoolPush")]
    public static extern IntPtr PushPool();

    [DllImport(Lib, EntryPoint = "objc_autoreleasePoolPop")]
    public static extern void PopPool(IntPtr pool);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, byte[] bytes, nuint length);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8);

    // BOOL is one byte on arm64 and x86_64 alike.
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendBool(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.U1)] bool arg);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SendReturningBool(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SendReturningBool(IntPtr receiver, IntPtr selector, out IntPtr error);

    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern int SendReturningInt(IntPtr receiver, IntPtr selector);
}
