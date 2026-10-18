using AppKit;

namespace LocalTake.Mac.Adapters;

internal static class AppActivation
{
    /// <summary>
    /// Brings LocalTake in front of the application in use, for the overlay, an editor or a dialog. macOS 14's
    /// cooperative activate may be declined while another application is in front, which is the usual case when a
    /// global shortcut fires: the keyboard (Escape, ⌘S, ⌘Q) would then still go to that application.
    /// </summary>
    public static void BringToFront()
    {
#pragma warning disable CA1422, CS0618 // Deprecated in macOS 14, and still the call that takes the focus in all cases.
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422, CS0618
    }
}
