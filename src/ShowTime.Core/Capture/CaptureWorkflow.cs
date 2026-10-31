using ShowTime.Core.Ports;
using ShowTime.Core.Recording;
using ShowTime.Core.Settings;

namespace ShowTime.Core.Capture;

/// <summary>Image captures from the shortcuts and the tray menu.</summary>
public sealed class CaptureWorkflow(
    AppSettings settings, ISettingsStore store, IAreaPicker picker, IScreen screen, CaptureDelivery delivery,
    IRecordingStatus recording, IUserDialogs dialogs, TimeProvider clock)
{
    /// <summary>Lets the tray menu fade out so that it does not appear in the capture.</summary>
    public static readonly TimeSpan MenuFadeOut = TimeSpan.FromMilliseconds(250);

    public bool IsCapturing { get; private set; }

    /// <summary>Freezes the screen, then a drag selects an area and a click takes a window.</summary>
    public Task CaptureAreaAsync(bool windowMode, bool fromMenu) => RunAsync(fromMenu, async () =>
    {
        if (await picker.PickAsync(windowMode) is not { } selection)
            return null;
        settings.LastRegion = selection.Area;
        store.TrySave(settings);
        return selection.Image;
    });

    public Task CaptureFullScreenAsync(bool fromMenu) =>
        RunAsync(fromMenu, () => Task.FromResult<IImage?>(screen.Capture(screen.VirtualScreen)));

    /// <summary>The area of the last selection, or a new selection when there is none yet.</summary>
    public Task CaptureLastRegionAsync(bool fromMenu)
    {
        if (settings.LastRegion is not { } last)
            return CaptureAreaAsync(windowMode: false, fromMenu);

        return RunAsync(fromMenu, () =>
        {
            // Monitors may have changed since the region was chosen.
            var area = last.Intersect(screen.VirtualScreen);
            return Task.FromResult(area.IsEmpty ? null : screen.Capture(area));
        });
    }

    private async Task RunAsync(bool fromMenu, Func<Task<IImage?>> capture)
    {
        // The selection overlay or the editor would end up in the video.
        if (IsCapturing || recording.IsBusy)
            return;
        IsCapturing = true;
        try
        {
            if (fromMenu)
                await Task.Delay(MenuFadeOut, clock);
            if (await capture() is { } image)
                delivery.Deliver(image);
        }
#pragma warning disable CA1031 // Whatever failed, the tray process must keep running and say so.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            dialogs.ShowError("La capture a échoué : " + ex.Message);
        }
        finally
        {
            IsCapturing = false;
        }
    }
}
