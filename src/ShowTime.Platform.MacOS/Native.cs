using System.Runtime.InteropServices;

namespace ShowTime.Platform.MacOS;

#pragma warning disable CA1707, IDE1006 // Apple names are kept as documented.

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public double X, Y, Width, Height;
}

/// <summary>CoreFoundation and CoreGraphics: the window list, and the Screen Recording permission.</summary>
internal static unsafe partial class Quartz
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    public const uint kCFStringEncodingUTF8 = 0x08000100;
    public const int kCFNumberSInt32Type = 3;
    public const uint kCGWindowListOptionOnScreenOnly = 1 << 0;
    public const uint kCGWindowListExcludeDesktopElements = 1 << 4;

    [LibraryImport(CoreFoundation)]
    public static partial void CFRelease(nint cf);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    [LibraryImport(CoreFoundation)]
    public static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundation)]
    public static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundation)]
    public static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CFNumberGetValue(nint number, int type, void* value);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CFBooleanGetValue(nint boolean);

    [LibraryImport(CoreGraphics)]
    public static partial int CGGetActiveDisplayList(uint maxDisplays, uint* displays, out uint displayCount);

    [LibraryImport(CoreGraphics)]
    public static partial nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGRectMakeWithDictionaryRepresentation(nint dictionary, out CGRect rect);

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGRequestScreenCaptureAccess();

    public static nint CFString(string value) => CFStringCreateWithCString(0, value, kCFStringEncodingUTF8);
}

/// <summary>Carbon's hot keys: the API that registers system-wide shortcuts without the Accessibility permission.</summary>
internal static unsafe partial class Carbon
{
    private const string Library = "/System/Library/Frameworks/Carbon.framework/Carbon";

    public const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
    public const uint kEventHotKeyPressed = 5;
    public const uint kEventHotKeyReleased = 6;
    public const uint kEventHotKeyExclusive = 1;
    public const uint kEventParamDirectObject = 0x2D2D2D2D; // '----'
    public const uint typeEventHotKeyID = 0x686B6964; // 'hkid'

    public const uint cmdKey = 0x0100;
    public const uint shiftKey = 0x0200;
    public const uint optionKey = 0x0800;
    public const uint controlKey = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventHotKeyID
    {
        public uint Signature;
        public uint Id;
    }

    [LibraryImport(Library)]
    public static partial nint GetApplicationEventTarget();

    [LibraryImport(Library)]
    public static partial int InstallEventHandler(nint target, delegate* unmanaged<nint, nint, nint, int> handler, nuint numTypes,
        EventTypeSpec* list, nint userData, out nint handlerRef);

    [LibraryImport(Library)]
    public static partial int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id, nint target, uint options, out nint hotKeyRef);

    [LibraryImport(Library)]
    public static partial int UnregisterEventHotKey(nint hotKeyRef);

    /// <summary>The shortcuts of macOS itself, as a CFArray of CFDictionary; released by the caller.</summary>
    [LibraryImport(Library)]
    public static partial int CopySymbolicHotKeys(out nint hotKeys);

    [LibraryImport(Library)]
    public static partial uint GetEventKind(nint evt);

    [LibraryImport(Library)]
    public static partial int GetEventParameter(nint evt, uint name, uint type, nint outActualType, nuint bufferSize, nint outActualSize, void* outData);
}

/// <summary>The C library: the FIFO through which ffmpeg reads the Mac's sound.</summary>
internal static partial class Libc
{
    private const string Library = "libc";

    public const int O_RDONLY = 0;
    public const int O_NONBLOCK = 0x0004;

    /// <param name="mode">mode_t, 16 bits on macOS.</param>
    [LibraryImport(Library, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int mkfifo(string path, ushort mode);

    [LibraryImport(Library, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int open(string path, int flags);

    [LibraryImport(Library)]
    public static partial int close(int fd);
}
