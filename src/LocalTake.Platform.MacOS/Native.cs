using System.Runtime.InteropServices;

namespace LocalTake.Platform.MacOS;

#pragma warning disable CA1707, IDE1006 // Apple names are kept as documented.

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGSize
{
    public double Width, Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;

    public CGRect(double x, double y, double width, double height)
    {
        Origin = new CGPoint { X = x, Y = y };
        Size = new CGSize { Width = width, Height = height };
    }
}

/// <summary>The Objective-C runtime: one entry point per message signature LocalTake sends.</summary>
internal static unsafe partial class ObjC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint objc_getClass(string name);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint sel_registerName(string name);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint arg);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint arg1, nint arg2);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint arg1, nint arg2, nint arg3);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, long arg);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint arg1, nuint arg2);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBool(nint receiver, nint selector, nint arg1, nint arg2);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBool(nint receiver, nint selector, nint arg1, nint arg2, nint arg3);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nuint SendNUInt(nint receiver, nint selector);

    public static nint Class(string name) => objc_getClass(name);

    public static nint Sel(string name) => sel_registerName(name);

    /// <summary>An autoreleased NSString.</summary>
    public static nint String(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>An autoreleased NSData holding a copy of the bytes.</summary>
    public static nint Data(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* data = bytes)
            return Send(Class("NSData"), Sel("dataWithBytes:length:"), (nint)data, (nuint)bytes.Length);
    }

    public static byte[] Bytes(nint data)
    {
        var length = (int)SendNUInt(data, Sel("length"));
        var bytes = new byte[length];
        Marshal.Copy(Send(data, Sel("bytes")), bytes, 0, length);
        return bytes;
    }
}

/// <summary>CoreFoundation, CoreGraphics and ImageIO.</summary>
internal static unsafe partial class Quartz
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    public const uint kCFStringEncodingUTF8 = 0x08000100;
    public const int kCFNumberSInt32Type = 3;
    public const uint kCGWindowListOptionOnScreenOnly = 1 << 0;
    public const uint kCGWindowListExcludeDesktopElements = 1 << 4;
    /// <summary>Premultiplied BGRA in memory: kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little.</summary>
    public const uint BgraBitmapInfo = 2 | (2 << 12);

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
    public static partial nint CFDataCreate(nint allocator, byte* bytes, nint length);

    [LibraryImport(CoreFoundation)]
    public static partial nint CFDataCreateMutable(nint allocator, nint capacity);

    [LibraryImport(CoreFoundation)]
    public static partial nint CFDataGetLength(nint data);

    [LibraryImport(CoreFoundation)]
    public static partial byte* CFDataGetBytePtr(nint data);

    [LibraryImport(CoreGraphics)]
    public static partial nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGRectMakeWithDictionaryRepresentation(nint dictionary, out CGRect rect);

    [LibraryImport(CoreGraphics)]
    public static partial uint CGMainDisplayID();

    [LibraryImport(CoreGraphics)]
    public static partial CGRect CGDisplayBounds(uint display);

    [LibraryImport(CoreGraphics)]
    public static partial nuint CGDisplayPixelsWide(uint display);

    [LibraryImport(CoreGraphics)]
    public static partial nint CGColorSpaceCreateDeviceRGB();

    [LibraryImport(CoreGraphics)]
    public static partial void CGColorSpaceRelease(nint space);

    [LibraryImport(CoreGraphics)]
    public static partial nint CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint space, uint bitmapInfo);

    [LibraryImport(CoreGraphics)]
    public static partial nint CGBitmapContextCreateImage(nint context);

    [LibraryImport(CoreGraphics)]
    public static partial void CGContextDrawImage(nint context, CGRect rect, nint image);

    [LibraryImport(CoreGraphics)]
    public static partial void CGContextRelease(nint context);

    [LibraryImport(CoreGraphics)]
    public static partial void CGImageRelease(nint image);

    [LibraryImport(CoreGraphics)]
    public static partial nuint CGImageGetWidth(nint image);

    [LibraryImport(CoreGraphics)]
    public static partial nuint CGImageGetHeight(nint image);

    [LibraryImport(ImageIO)]
    public static partial nint CGImageSourceCreateWithData(nint data, nint options);

    [LibraryImport(ImageIO)]
    public static partial nint CGImageSourceCreateImageAtIndex(nint source, nuint index, nint options);

    [LibraryImport(ImageIO)]
    public static partial nint CGImageDestinationCreateWithData(nint data, nint type, nuint count, nint options);

    [LibraryImport(ImageIO)]
    public static partial void CGImageDestinationAddImage(nint destination, nint image, nint properties);

    [LibraryImport(ImageIO)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool CGImageDestinationFinalize(nint destination);

    public static nint CFString(string value) => CFStringCreateWithCString(0, value, kCFStringEncodingUTF8);
}

/// <summary>Carbon's hot keys: the API that registers system-wide shortcuts without the Accessibility permission.</summary>
internal static unsafe partial class Carbon
{
    private const string Library = "/System/Library/Frameworks/Carbon.framework/Carbon";

    public const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
    public const uint kEventHotKeyPressed = 5;
    public const uint kEventHotKeyReleased = 6;
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

    [LibraryImport(Library)]
    public static partial uint GetEventKind(nint evt);

    [LibraryImport(Library)]
    public static partial int GetEventParameter(nint evt, uint name, uint type, nint outActualType, nuint bufferSize, nint outActualSize, void* outData);
}
