using System.IO.Abstractions.TestingHelpers;
using LocalTake.Core.Capture;
using LocalTake.Core.Files;
using LocalTake.Core.Geometry;
using LocalTake.Core.Ports;
using LocalTake.Core.Recording;
using LocalTake.Core.Settings;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LocalTake.Core.Tests.Capture;

public sealed class CaptureWorkflowTests
{
    private static readonly PixelRect VirtualScreen = new(-1920, 0, 3840, 1080);
    private static readonly string Out = Path.Combine(Path.GetTempPath(), "LocalTakeWorkflow");

    private readonly AppSettings _settings = new(SettingsDefaults.ForWindows(Out));
    private readonly MemorySettingsStore _store = new();
    private readonly IAreaPicker _picker = Substitute.For<IAreaPicker>();
    private readonly IScreen _screen = Substitute.For<IScreen>();
    private readonly IRecordingStatus _recording = Substitute.For<IRecordingStatus>();
    private readonly IUserDialogs _dialogs = Substitute.For<IUserDialogs>();
    private readonly IEditorLauncher _editor = Substitute.For<IEditorLauncher>();
    private readonly IClipboard _clipboard = Substitute.For<IClipboard>();
    private readonly INotifications _notifications = Substitute.For<INotifications>();
    private readonly IImageCodec _codec = Substitute.For<IImageCodec>();
    private readonly FakeTimeProvider _clock = new();
    private readonly CaptureWorkflow _workflow;

    public CaptureWorkflowTests()
    {
        _screen.VirtualScreen.Returns(VirtualScreen);
        _screen.Capture(Arg.Any<PixelRect>()).Returns(_ => new FakeImage());
        var fs = new MockFileSystem();
        var saver = new ImageSaver(_settings, _store, new OutputPathAllocator(fs, _clock), new FileCounter(_settings, _store), _codec, _clipboard, fs);
        var delivery = new CaptureDelivery(_settings, saver, _clipboard, _editor, _notifications);
        _workflow = new CaptureWorkflow(_settings, _store, _picker, _screen, delivery, _recording, _dialogs, _clock);
    }

    private FakeImage PickReturns(PixelRect area)
    {
        var image = new FakeImage();
        _picker.PickAsync(Arg.Any<bool>()).Returns(new AreaSelection(area, image));
        return image;
    }

    [Fact]
    public async Task An_area_capture_opens_the_editor_and_remembers_the_region()
    {
        var image = PickReturns(new PixelRect(1, 2, 30, 40));

        await _workflow.CaptureAreaAsync(windowMode: false, fromMenu: false);

        _editor.Received(1).Open(image);
        _settings.LastRegion.ShouldBe(new PixelRect(1, 2, 30, 40));
        image.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task Window_mode_is_passed_to_the_picker()
    {
        PickReturns(new PixelRect(1, 2, 30, 40));
        await _workflow.CaptureAreaAsync(windowMode: true, fromMenu: false);
        await _picker.Received(1).PickAsync(true);
    }

    [Fact]
    public async Task A_cancelled_selection_delivers_nothing()
    {
        _picker.PickAsync(Arg.Any<bool>()).Returns((AreaSelection?)null);
        await _workflow.CaptureAreaAsync(false, false);
        _editor.DidNotReceiveWithAnyArgs().Open(default!);
        _settings.LastRegion.ShouldBeNull();
    }

    [Fact]
    public async Task Captures_are_refused_while_a_video_session_is_under_way()
    {
        // The selection overlay or the editor would end up in the video.
        _recording.IsBusy.Returns(true);
        await _workflow.CaptureAreaAsync(false, false);
        await _picker.DidNotReceiveWithAnyArgs().PickAsync(default);
    }

    [Fact]
    public async Task A_second_capture_is_ignored_while_one_is_under_way()
    {
        var pending = new TaskCompletionSource<AreaSelection?>();
        _picker.PickAsync(Arg.Any<bool>()).Returns(pending.Task);

        var first = _workflow.CaptureAreaAsync(false, false);
        _workflow.IsCapturing.ShouldBeTrue();
        await _workflow.CaptureFullScreenAsync(false);
        pending.SetResult(null);
        await first;

        _screen.DidNotReceiveWithAnyArgs().Capture(default);
        _workflow.IsCapturing.ShouldBeFalse();
    }

    [Fact]
    public async Task From_the_menu_the_capture_waits_for_the_menu_to_fade_out()
    {
        PickReturns(new PixelRect(0, 0, 5, 5));
        var capture = _workflow.CaptureAreaAsync(false, fromMenu: true);
        await _picker.DidNotReceiveWithAnyArgs().PickAsync(default);
        await _clock.RunUntil(capture);
        await _picker.Received(1).PickAsync(false);
    }

    [Fact]
    public async Task Full_screen_captures_the_whole_virtual_desktop()
    {
        await _workflow.CaptureFullScreenAsync(false);
        _screen.Received(1).Capture(VirtualScreen);
    }

    [Fact]
    public async Task Last_region_is_clipped_to_the_current_monitors()
    {
        _settings.LastRegion = new PixelRect(-2000, 100, 200, 50);
        await _workflow.CaptureLastRegionAsync(false);
        _screen.Received(1).Capture(new PixelRect(-1920, 100, 120, 50));
    }

    [Fact]
    public async Task Last_region_entirely_off_screen_captures_nothing()
    {
        _settings.LastRegion = new PixelRect(5000, 0, 10, 10);
        await _workflow.CaptureLastRegionAsync(false);
        _screen.DidNotReceiveWithAnyArgs().Capture(default);
    }

    [Fact]
    public async Task Without_a_last_region_an_area_is_selected()
    {
        PickReturns(new PixelRect(0, 0, 5, 5));
        await _workflow.CaptureLastRegionAsync(false);
        await _picker.Received(1).PickAsync(false);
    }

    [Fact]
    public async Task A_failed_capture_is_reported()
    {
        _screen.Capture(Arg.Any<PixelRect>()).Throws(new InvalidOperationException("GDI"));
        await _workflow.CaptureFullScreenAsync(false);
        _dialogs.Received(1).ShowError("La capture a échoué : GDI");
        _workflow.IsCapturing.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveToFile_saves_and_notifies_then_releases_the_image()
    {
        _settings.AfterCapture = AfterCaptureAction.SaveToFile;
        var image = PickReturns(new PixelRect(0, 0, 5, 5));
        await _workflow.CaptureAreaAsync(false, false);

        _codec.ReceivedWithAnyArgs(1).Save(default!, default!, default);
        _notifications.Received(1).FileSaved(Arg.Is<string>(p => p.StartsWith(Out, StringComparison.Ordinal)));
        image.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task CopyToClipboard_copies_and_tells_so()
    {
        _settings.AfterCapture = AfterCaptureAction.CopyToClipboard;
        var image = PickReturns(new PixelRect(0, 0, 5, 5));
        await _workflow.CaptureAreaAsync(false, false);

        _clipboard.Received(1).SetImage(image);
        _notifications.Received(1).Info("Capture copiée", "L'image est dans le presse-papier.");
        _codec.DidNotReceiveWithAnyArgs().Save(default!, default!, default);
    }

    [Fact]
    public async Task SaveAndCopy_copies_the_image_after_the_path_so_that_the_image_stays_in_the_clipboard()
    {
        _settings.AfterCapture = AfterCaptureAction.SaveAndCopy;
        _settings.CopyPathAfterSave = true;
        var image = PickReturns(new PixelRect(0, 0, 5, 5));
        await _workflow.CaptureAreaAsync(false, false);

        Received.InOrder(() =>
        {
            _clipboard.SetText(Arg.Any<string>());
            _clipboard.SetImage(image);
        });
        _notifications.ReceivedWithAnyArgs(1).FileSaved(default!);
    }
}
