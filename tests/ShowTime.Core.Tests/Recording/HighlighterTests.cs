using Microsoft.Extensions.Time.Testing;
using ShowTime.Core.Geometry;
using ShowTime.Core.Hotkeys;
using ShowTime.Core.Recording;
using ShowTime.Core.Settings;

namespace ShowTime.Core.Tests.Recording;

public sealed class HighlighterTests
{
    private static readonly PixelRect Area = new(0, 0, 800, 600);

    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows("C:\\Out"));
    private readonly FakeHighlightInput _input = new();
    private readonly FakeHighlightOverlayFactory _overlays = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly Highlighter _highlighter;

    public HighlighterTests() =>
        _highlighter = new Highlighter(_settings, _input, _overlays, new ImmediateUiThread(), _clock, HotkeyNaming.Windows);

    private FakeHighlightOverlay Overlay => _overlays.Overlay!;

    private void HoldAt(PixelPoint point)
    {
        _highlighter.Enable(Area);
        _input.Cursor = point;
        _input.IsHeld = true;
        _input.Press();
    }

    private void Advance(TimeSpan duration)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += Highlighter.FrameInterval)
            _clock.Advance(Highlighter.FrameInterval);
    }

    private void Release()
    {
        _input.IsHeld = false;
        _clock.Advance(Highlighter.FrameInterval);
    }

    [Fact]
    public void Enabling_registers_the_trigger_and_returns_how_the_bar_shows_it()
    {
        _highlighter.Enable(Area).ShouldBe("Ctrl+Space");
        _input.Registered.ShouldBe(HighlightTrigger.FromKey(new Hotkey(HotkeyModifiers.Control, KeyCode.Space)));
        _overlays.Area.ShouldBe(Area);
        Overlay.Visible.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Foo")]
    public void An_empty_or_invalid_trigger_enables_nothing(string text)
    {
        _settings.HighlightTrigger = text;
        _highlighter.Enable(Area).ShouldBeNull();
        _input.Registered.ShouldBeNull();
        _overlays.Overlay.ShouldBeNull();
    }

    [Fact]
    public void A_refused_trigger_enables_nothing()
    {
        _input.Refused = true;
        _highlighter.Enable(Area).ShouldBeNull();
        _overlays.Overlay.ShouldBeNull();
    }

    [Fact]
    public void A_side_button_is_shown_by_its_French_name()
    {
        _settings.HighlightTrigger = "XButton1";
        _highlighter.Enable(Area).ShouldBe("Souris 4");
    }

    [Fact]
    public void Holding_draws_a_trail_that_follows_the_pointer()
    {
        HoldAt(new PixelPoint(100, 100));
        Overlay.Visible.ShouldBeTrue();
        Overlay.Opacity.ShouldBe(HighlightTrail.FullOpacity);
        Overlay.Draws[^1].ShouldBe([new PixelPoint(100, 100)]);

        _input.Cursor = new PixelPoint(110, 100);
        _clock.Advance(Highlighter.FrameInterval);
        Overlay.Draws[^1].ShouldBe([new PixelPoint(100, 100), new PixelPoint(110, 100)]);
    }

    [Fact]
    public void A_still_pointer_draws_nothing_new()
    {
        HoldAt(new PixelPoint(100, 100));
        var draws = Overlay.Draws.Count;
        Advance(TimeSpan.FromMilliseconds(200));
        Overlay.Draws.Count.ShouldBe(draws);
    }

    [Fact]
    public void Releasing_fades_the_trail_then_hides_it_and_stops_the_frames()
    {
        HoldAt(new PixelPoint(100, 100));
        Release();

        // The trail stays 300 ms as it is, then fades out in 500 ms.
        Advance(TimeSpan.FromMilliseconds(250));
        Overlay.Opacity.ShouldBe(HighlightTrail.FullOpacity);
        Overlay.Visible.ShouldBeTrue();

        Advance(TimeSpan.FromMilliseconds(300));
        Overlay.Opacity.ShouldBeInRange(0.03, 0.09);
        Overlay.Visible.ShouldBeTrue();

        Advance(TimeSpan.FromMilliseconds(300));
        Overlay.Visible.ShouldBeFalse();
        var changes = Overlay.OpacityChanges;
        Advance(TimeSpan.FromSeconds(1));
        Overlay.OpacityChanges.ShouldBe(changes);
    }

    [Fact]
    public void Pressing_again_during_the_fade_starts_a_new_trail_at_full_opacity()
    {
        HoldAt(new PixelPoint(100, 100));
        Release();
        Advance(TimeSpan.FromMilliseconds(100));

        _input.Cursor = new PixelPoint(300, 300);
        _input.IsHeld = true;
        _input.Press();
        Overlay.Opacity.ShouldBe(HighlightTrail.FullOpacity);
        Overlay.Draws[^1].ShouldBe([new PixelPoint(300, 300)]);

        // The fade is over: the new trail keeps following the pointer.
        _input.Cursor = new PixelPoint(320, 300);
        Advance(TimeSpan.FromMilliseconds(600));
        Overlay.Visible.ShouldBeTrue();
        Overlay.Draws[^1].ShouldBe([new PixelPoint(300, 300), new PixelPoint(320, 300)]);
    }

    [Fact]
    public void A_repeated_press_while_held_keeps_the_trail()
    {
        HoldAt(new PixelPoint(100, 100));
        _input.Cursor = new PixelPoint(120, 100);
        _clock.Advance(Highlighter.FrameInterval);
        _input.Press();
        Overlay.Draws[^1].ShouldBe([new PixelPoint(100, 100), new PixelPoint(120, 100)]);
    }

    [Fact]
    public void Disabling_while_held_closes_the_overlay_and_stops_the_frames()
    {
        HoldAt(new PixelPoint(100, 100));
        var draws = Overlay.Draws.Count;

        _highlighter.Disable();
        Overlay.Disposed.ShouldBeTrue();
        _input.Unregistrations.ShouldBe(1);

        _input.Cursor = new PixelPoint(200, 200);
        Advance(TimeSpan.FromSeconds(1));
        Overlay.Draws.Count.ShouldBe(draws);
    }

    [Fact]
    public void Enabling_again_replaces_the_previous_overlay()
    {
        _highlighter.Enable(Area);
        var first = Overlay;
        _highlighter.Enable(new PixelRect(10, 10, 100, 100));
        first.Disposed.ShouldBeTrue();
        _overlays.Overlay.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void Disposing_disables()
    {
        _highlighter.Enable(Area);
        _highlighter.Dispose();
        Overlay.Disposed.ShouldBeTrue();
    }
}
