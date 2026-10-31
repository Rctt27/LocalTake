using CoreGraphics;
using Foundation;
using ScreenCaptureKit;
using ShowTime.Core.Geometry;
using ShowTime.Core.Ports;
using ShowTime.Mac.Adapters;
using ShowTime.Platform.MacOS;

namespace ShowTime.Mac.Platform;

/// <summary>
/// Screen captures through ScreenCaptureKit, one screenshot per screen the area spans, at each screen's full
/// resolution, assembled in ShowTime's pixel space.
/// </summary>
internal sealed class MacScreenGrabber : IScreenGrabber
{
    public const string PermissionMessage = "ShowTime n'a pas l'autorisation d'enregistrer l'écran. Ouvrez Réglages Système > " +
        "Confidentialité et sécurité > Enregistrement de l'écran, cochez ShowTime, puis relancez-le.";

    /// <summary>SCStreamErrorUserDeclined.</summary>
    private const long UserDeclined = -3801;

    public PixelBuffer Grab(PixelRect area)
    {
        EnsurePermission();
        var screens = ScreenInfo.Current().Where(screen => !screen.Pixels.Intersect(area).IsEmpty).ToList();
        var content = Wait<SCShareableContent>(done =>
            SCShareableContent.GetShareableContent(true, true, (result, error) => done(result, error)));
        var captures = new List<(PixelRect, PixelBuffer)>();
        foreach (var screen in screens)
        {
            var display = content.Displays.FirstOrDefault(d => d.DisplayId == screen.DisplayId);
            if (display is null)
                continue;
            using var image = Capture(display, screen.Pixels);
            captures.Add((screen.Pixels, MacImage.ToPixels(image)));
        }
        return MacScreenSpace.Compose(area, captures);
    }

    public IReadOnlyList<PixelRect> VisibleWindows()
    {
        var screens = ScreenInfo.Current();
        var frames = screens.Select(s => s.Frame).ToArray();
        var layout = screens.Select(s => s.Pixels).ToArray();
        return [.. MacWindowList.OnScreen()
            .Select(w => MacScreenSpace.FromTopLeftPoints(frames, layout, w.X, w.Y, w.Width, w.Height))
            .Where(w => w.Width > 1 && w.Height > 1)];
    }

    /// <summary>Without the permission, macOS hands back the wallpaper alone: better to say what to do.</summary>
    private static void EnsurePermission()
    {
        if (ScreenCaptureAccess.IsGranted)
            return;
        ScreenCaptureAccess.Request();
        throw new UnauthorizedAccessException(PermissionMessage);
    }

    private static CGImage Capture(SCDisplay display, PixelRect pixels)
    {
        using var filter = new SCContentFilter(display, [], SCContentFilterOption.Exclude);
        using var configuration = new SCStreamConfiguration
        {
            Width = (nuint)pixels.Width,
            Height = (nuint)pixels.Height,
            ShowsCursor = false
        };
        return Wait<CGImage>(done => SCScreenshotManager.CaptureImage(filter, configuration, (image, error) => done(image, error)));
    }

    /// <summary>Waits for a ScreenCaptureKit completion handler.</summary>
    private static T Wait<T>(Action<Action<T?, NSError?>> start) where T : class
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        start((result, error) =>
        {
            if (result is not null)
                completion.TrySetResult(result);
            else
                completion.TrySetException(error?.Code == UserDeclined
                    ? new UnauthorizedAccessException(PermissionMessage)
                    : new InvalidOperationException("La capture d'écran a échoué : " + error?.LocalizedDescription));
        });
        // The handler may come through the main queue: keep the main run loop turning rather than blocking it.
        while (!completion.Task.IsCompleted && NSThread.IsMain)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(0.01));
        return completion.Task.GetAwaiter().GetResult();
    }
}
