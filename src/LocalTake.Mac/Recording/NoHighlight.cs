using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;

namespace LocalTake.Mac.Recording;

/// <summary>
/// The mouse highlight is not on macOS yet: the trigger is never registered, so the bar shows no hint and no overlay
/// is ever made. See the "Surlignage" section of docs/superpowers/plans/2026-10-17-macos-native.md.
/// </summary>
internal sealed class NoHighlightInput : IHighlightInput
{
    public bool IsHeld => false;

    public PixelPoint Cursor => default;

    public bool Register(HighlightTrigger trigger, Action pressed) => false;

    public void Unregister()
    {
    }
}

internal sealed class NoHighlightOverlayFactory : IHighlightOverlayFactory
{
    public IHighlightOverlay Create(PixelRect area) =>
        throw new InvalidOperationException("The mouse highlight is not available on macOS yet.");
}
