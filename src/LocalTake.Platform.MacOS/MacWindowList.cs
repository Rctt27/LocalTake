namespace LocalTake.Platform.MacOS;

/// <summary>A window's frame in Quartz coordinates: points, origin at the top left of the main screen, Y going down.</summary>
public sealed record WindowFrame(double X, double Y, double Width, double Height);

/// <summary>
/// The windows on screen, front to back, from the window server. ScreenCaptureKit lists windows too, but in no
/// stated order, and picking a window needs the one in front.
/// </summary>
public static unsafe class MacWindowList
{
    /// <summary>Ordinary windows (layer 0): the menu bar, the Dock and overlays are above that layer.</summary>
    public static IReadOnlyList<WindowFrame> OnScreen()
    {
        var windows = new List<WindowFrame>();
        var list = Quartz.CGWindowListCopyWindowInfo(Quartz.kCGWindowListOptionOnScreenOnly | Quartz.kCGWindowListExcludeDesktopElements, 0);
        if (list == 0)
            return windows;
        var boundsKey = Quartz.CFString("kCGWindowBounds");
        var layerKey = Quartz.CFString("kCGWindowLayer");
        try
        {
            var count = Quartz.CFArrayGetCount(list);
            for (nint i = 0; i < count; i++)
            {
                var info = Quartz.CFArrayGetValueAtIndex(list, i);
                var layer = 0;
                var layerValue = Quartz.CFDictionaryGetValue(info, layerKey);
                if (layerValue != 0)
                    Quartz.CFNumberGetValue(layerValue, Quartz.kCFNumberSInt32Type, &layer);
                if (layer != 0)
                    continue;
                var boundsValue = Quartz.CFDictionaryGetValue(info, boundsKey);
                if (boundsValue != 0 && Quartz.CGRectMakeWithDictionaryRepresentation(boundsValue, out var r))
                    windows.Add(new WindowFrame(r.X, r.Y, r.Width, r.Height));
            }
        }
        finally
        {
            Quartz.CFRelease(boundsKey);
            Quartz.CFRelease(layerKey);
            Quartz.CFRelease(list);
        }
        return windows;
    }
}

/// <summary>The "Screen Recording" permission, which every capture needs.</summary>
public static class ScreenCaptureAccess
{
    /// <summary>
    /// Whether LocalTake may capture the screen. A permission granted while LocalTake runs only counts after a restart.
    /// </summary>
    public static bool IsGranted => Quartz.CGPreflightScreenCaptureAccess();

    /// <summary>Asks macOS to offer the permission: the first time, it shows its own dialog.</summary>
    public static void Request() => Quartz.CGRequestScreenCaptureAccess();
}
