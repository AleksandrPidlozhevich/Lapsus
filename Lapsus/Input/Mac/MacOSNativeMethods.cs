using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacOSNativeMethods
{
    static MacOSNativeMethods()
    {
        var coreFoundation = NativeLibrary.Load(
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation");
        RunLoopDefaultMode = ReadGlobalConstant(coreFoundation, "kCFRunLoopDefaultMode");
        CFBooleanTrue = ReadGlobalConstant(coreFoundation, "kCFBooleanTrue");
        DictionaryKeyCallBacks = ReadGlobalConstant(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
        DictionaryValueCallBacks = ReadGlobalConstant(coreFoundation, "kCFTypeDictionaryValueCallBacks");

        var carbon = NativeLibrary.Load("/System/Library/Frameworks/Carbon.framework/Carbon");
        PropertyInputSourceId = ReadGlobalConstant(carbon, "kTISPropertyInputSourceID");
        PropertyUnicodeKeyLayoutData = ReadGlobalConstant(carbon, "kTISPropertyUnicodeKeyLayoutData");
        PropertyInputSourceLanguages = ReadGlobalConstant(carbon, "kTISPropertyInputSourceLanguages");

        var appServices = NativeLibrary.Load(
            "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices");

        TrustedCheckOptionPromptKey = ReadGlobalConstant(appServices, "kAXTrustedCheckOptionPrompt");

        AxFocusedUiElementChangedNotification = CreateCFString("AXFocusedUIElementChanged");
        AxFocusedWindowChangedNotification = CreateCFString("AXFocusedWindowChanged");
        AxManualAccessibilityAttribute = CreateCFString("AXManualAccessibility");
        AxFocusedUiElementAttribute = CreateCFString("AXFocusedUIElement");
        AxRoleAttribute = CreateCFString("AXRole");
        AxSelectedTextRangeAttribute = CreateCFString("AXSelectedTextRange");
        AxSelectedTextAttribute = CreateCFString("AXSelectedText");
        AxValueAttribute = CreateCFString("AXValue");
        AxBoundsForRangeParameterizedAttribute = CreateCFString("AXBoundsForRangeParameterized");
    }

    private static IntPtr ReadGlobalConstant(IntPtr library, string symbolName)
    {
        var symbol = NativeLibrary.GetExport(library, symbolName);
        return Marshal.ReadIntPtr(symbol);
    }

    private static IntPtr CreateCFString(string value)
    {
        return CFStringCreateWithCString(IntPtr.Zero, value, CFStringEncodingUtf8);
    }

    public static IntPtr RunLoopDefaultMode { get; }

    public static IntPtr PropertyInputSourceId { get; }

    public static IntPtr PropertyUnicodeKeyLayoutData { get; }

    public static IntPtr PropertyInputSourceLanguages { get; }

    public static IntPtr CFBooleanTrue { get; }

    public static IntPtr TrustedCheckOptionPromptKey { get; }

    public static IntPtr AxFocusedUiElementChangedNotification { get; }

    public static IntPtr AxFocusedWindowChangedNotification { get; }

    public static IntPtr AxManualAccessibilityAttribute { get; }

    public static IntPtr AxFocusedUiElementAttribute { get; }

    public static IntPtr AxRoleAttribute { get; }

    public static IntPtr AxSelectedTextRangeAttribute { get; }

    public static IntPtr AxSelectedTextAttribute { get; }

    public static IntPtr AxValueAttribute { get; }

    public static IntPtr AxBoundsForRangeParameterizedAttribute { get; }

    public static IntPtr CgWindowOwnerName => CgWindowKeys.OwnerName;

    public static IntPtr CgWindowOwnerPid => CgWindowKeys.OwnerPid;

    public static IntPtr CgWindowLayer => CgWindowKeys.Layer;

    private static IntPtr DictionaryKeyCallBacks { get; }

    private static IntPtr DictionaryValueCallBacks { get; }

    private static class CgWindowKeys
    {
        internal static readonly IntPtr OwnerName;
        internal static readonly IntPtr OwnerPid;
        internal static readonly IntPtr Layer;

        static CgWindowKeys()
        {
            try
            {
                var coreGraphics = NativeLibrary.Load(
                    "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics");
                OwnerName = ReadGlobalConstant(coreGraphics, "kCGWindowOwnerName");
                OwnerPid = ReadGlobalConstant(coreGraphics, "kCGWindowOwnerPID");
                Layer = ReadGlobalConstant(coreGraphics, "kCGWindowLayer");
            }
            catch (Exception)
            {

                OwnerName = OwnerPid = Layer = IntPtr.Zero;
            }
        }
    }

    private static IntPtr? _accessibilityPromptOptions;

    public const ushort UCKeyActionDisplay = 3;

    public const ushort UCKeyActionDown = 0;

    public const ushort UCKeyActionAutoKey = 2;

    public const uint UCKeyTranslateNoDeadKeys = 1U << 0;

    public const int SpaceKeyCode = 0x31;
    public const int DeleteKeyCode = 0x33;
    public const int ForwardDeleteKeyCode = 0x75;

    public const int TabKeyCode = 0x30;
    public const int EscapeKeyCode = 0x35;
    public const int HomeKeyCode = 0x73;
    public const int PageUpKeyCode = 0x74;
    public const int EndKeyCode = 0x77;
    public const int PageDownKeyCode = 0x79;
    public const int LeftArrowKeyCode = 0x7B;
    public const int RightArrowKeyCode = 0x7C;
    public const int DownArrowKeyCode = 0x7D;
    public const int UpArrowKeyCode = 0x7E;

    public const int ShiftKeyCode = 0x38;
    public const int RightShiftKeyCode = 0x3C;
    public const int ControlKeyCode = 0x3B;
    public const int RightControlKeyCode = 0x3E;
    public const int OptionKeyCode = 0x3A;
    public const int RightOptionKeyCode = 0x3D;
    public const int CommandKeyCode = 0x37;
    public const int RightCommandKeyCode = 0x36;

    public const ulong EventFlagMaskAlphaShift = 1UL << 16;
    public const ulong EventFlagMaskShift = 1UL << 17;
    public const ulong EventFlagMaskControl = 1UL << 18;
    public const ulong EventFlagMaskAlternate = 1UL << 19;
    public const ulong EventFlagMaskCommand = 1UL << 20;

    public const int EventTapDisabledByTimeout = unchecked((int)0xFFFFFFFE);
    public const int EventTapDisabledByUserInput = unchecked((int)0xFFFFFFFD);

    public const uint EventMaskKeyDown = 1U << 10;
    public const uint EventMaskKeyUp = 1U << 11;
    public const uint EventMaskLeftMouseDown = 1U << 1;
    public const uint EventMaskRightMouseDown = 1U << 3;
    public const uint EventMaskOtherMouseDown = 1U << 25;
    public const uint EventMaskFlagsChanged = 1U << 12;

    public const int EventKeyDown = 10;
    public const int EventKeyUp = 11;
    public const int EventFlagsChanged = 12;
    public const int EventLeftMouseDown = 1;
    public const int EventRightMouseDown = 3;
    public const int EventOtherMouseDown = 25;

    public const int EventKeyboardAutorepeat = 8;
    public const int EventKeyboardKeycode = 9;
    public const int EventSourceUnixProcessId = 41;
    public const int EventSourceUserData = 42;

    public const long InjectedMarker = 0x4C50_5355;

    public const uint CFStringEncodingUtf8 = 0x0800_0100;

    public delegate IntPtr EventTapCallback(IntPtr proxy, int type, IntPtr @event, IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern IntPtr CGEventTapCreate(
        uint tap,
        uint place,
        uint options,
        uint eventsOfInterest,
        EventTapCallback callback,
        IntPtr userInfo);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern void CGEventSetIntegerValueField(IntPtr @event, int field, long value);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern long CGEventGetIntegerValueField(IntPtr @event, int field);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern ulong CGEventGetFlags(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern void CGEventKeyboardSetUnicodeString(IntPtr @event, long stringLength, byte[] unicodeString);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern void CGEventPost(uint tap, IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern void CGEventKeyboardGetUnicodeString(
        IntPtr @event,
        long maxStringLength,
        out long actualStringLength,
        [Out] byte[] unicodeString);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, int order);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFRunLoopGetCurrent();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern void CFRunLoopRun();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern void CFRunLoopStop(IntPtr rl);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern void CFRetain(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern long CFDataGetLength(IntPtr theData);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFDataGetBytePtr(IntPtr theData);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, long length);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string cStr, uint encoding);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int PasteboardCreate(IntPtr name, out IntPtr outPasteboard);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern uint PasteboardSynchronize(IntPtr pasteboard);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int PasteboardClear(IntPtr pasteboard);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int PasteboardGetItemCount(IntPtr pasteboard, out uint outItemCount);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int PasteboardGetItemIdentifier(IntPtr pasteboard, uint itemIndex, out IntPtr outItem);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int PasteboardCopyItemFlavorData(
        IntPtr pasteboard, IntPtr item, IntPtr flavorType, out IntPtr outData);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int PasteboardPutItemFlavor(
        IntPtr pasteboard, IntPtr item, IntPtr flavorType, IntPtr data, uint flags);

    public const uint PasteboardModified = 1;

    public const ushort CKeyCode = 0x08;

    public const ushort VKeyCode = 0x09;

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    public static extern IntPtr TISCopyCurrentKeyboardInputSource();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsSecureEventInputEnabled();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    public static extern IntPtr TISCreateInputSourceList(IntPtr properties, bool includeAllInstalled);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    public static extern IntPtr TISGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    public static extern int TISSelectInputSource(IntPtr inputSource);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    public static extern uint LMGetKbdType();

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    public static extern int UCKeyTranslate(
        IntPtr keyLayoutPtr,
        ushort virtualKeyCode,
        ushort keyAction,
        uint modifierKeyState,
        uint keyboardType,
        uint keyTranslateOptions,
        ref uint deadKeyState,
        uint maxStringLength,
        ref ushort actualStringLength,
        [Out] char[] unicodeString);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern long CFArrayGetCount(IntPtr theArray);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, long idx);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFStringGetCStringPtr(IntPtr theString, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool
        CFStringGetCString(IntPtr theString, StringBuilder buffer, long bufferSize, uint encoding);

    [DllImport("/usr/lib/libproc.dylib")]
    public static extern int proc_name(int pid, StringBuilder buffer, uint bufferSize);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrusted();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    public const uint HidRequestTypeListenEvent = 0;
    public const uint HidAccessTypeGranted = 0;

    // Input Monitoring (IOKit): TCC permission CGEventTapCreate needs.
    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    public static extern uint IOHIDCheckAccess(uint requestType);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool IOHIDRequestAccess(uint requestType);

    public delegate void AXObserverCallback(
        IntPtr observer, IntPtr element, IntPtr notification, IntPtr refcon);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXUIElementSetMessagingTimeout(IntPtr element, float timeoutInSeconds);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXUIElementCopyAttributeValue(
        IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXUIElementSetAttributeValue(
        IntPtr element, IntPtr attribute, IntPtr value);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport("/usr/lib/libproc.dylib")]
    public static extern int proc_pidpath(int pid, StringBuilder buffer, uint buffersize);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXUIElementCopyParameterizedAttributeValue(
        IntPtr element, IntPtr parameterizedAttribute, IntPtr parameter, out IntPtr value);

    public const uint AxValueTypeCgRect = 3;
    public const uint AxValueTypeCfRange = 4;

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern IntPtr AXValueCreate(uint theType, ref CFRange valuePtr);

    [DllImport(
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices",
        EntryPoint = "AXValueGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXValueGetCGRect(IntPtr value, uint theType, out CGRect valuePtr);

    [DllImport(
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices",
        EntryPoint = "AXValueGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXValueGetCFRange(IntPtr value, uint theType, out CFRange valuePtr);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int PasteboardCopyItemFlavors(
        IntPtr pasteboard, IntPtr item, out IntPtr outFlavorArray);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXUIElementGetPid(IntPtr element, out int pid);

    public const uint CgWindowListOptionOnScreenOnly = 1;
    public const uint CgWindowListExcludeDesktopElements = 1U << 4;
    public const int CfNumberIntType = 9;

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    public static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFDictionaryGetValue(IntPtr theDict, IntPtr key);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CFNumberGetValue(IntPtr number, int theType, out int valuePtr);

    public static string? ReadCfString(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero)
            return null;

        var direct = CFStringGetCStringPtr(cfString, CFStringEncodingUtf8);
        if (direct != IntPtr.Zero)
            return Marshal.PtrToStringUTF8(direct);

        var sb = new StringBuilder(256);
        return CFStringGetCString(cfString, sb, sb.Capacity, CFStringEncodingUtf8)
            ? sb.ToString()
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXObserverCreate(
        int application, AXObserverCallback callback, out IntPtr outObserver);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXObserverAddNotification(
        IntPtr observer, IntPtr element, IntPtr notification, IntPtr refcon);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern int AXObserverRemoveNotification(
        IntPtr observer, IntPtr element, IntPtr notification);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    public static extern IntPtr AXObserverGetRunLoopSource(IntPtr observer);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    public static extern int getpid();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern nint CFHash(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    public static extern IntPtr CFDictionaryCreate(
        IntPtr allocator,
        IntPtr[] keys,
        IntPtr[] values,
        long numValues,
        IntPtr keyCallBacks,
        IntPtr valueCallBacks);

    public static IntPtr GetAccessibilityPromptOptions()
    {
        if (_accessibilityPromptOptions is { } cached)
            return cached;

        var keys = new[] { TrustedCheckOptionPromptKey };
        var values = new[] { CFBooleanTrue };
        cached = CFDictionaryCreate(
            IntPtr.Zero, keys, values, 1, DictionaryKeyCallBacks, DictionaryValueCallBacks);
        _accessibilityPromptOptions = cached;
        return cached;
    }
}
