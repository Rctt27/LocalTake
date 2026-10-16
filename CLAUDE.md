# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

LocalTake is a tray screen-capture and screen-recording tool modelled on TinyTake, with every remote feature removed. **Captures must never leave the machine**: no network calls, no upload, no telemetry. This is also why the default output folder avoids a OneDrive-synced Pictures folder (`AppPaths.WindowsOutputFolder`), and why the log is a local file only.

It runs on Windows today; the macOS version is under way (see Architecture).

UI strings are French; code and comments are English.

## Commands

Requires the .NET 10 SDK (pinned in `global.json`). Analyzers run at `latest-recommended` with warnings as errors; `.editorconfig` holds the style, and CI (`.github/workflows/ci.yml`) rejects unformatted code.

```
dotnet build LocalTake.slnx -c Release
dotnet test --solution LocalTake.slnx                 # every test project (Microsoft.Testing.Platform, xunit v3)
dotnet test --project tests/LocalTake.Core.Tests      # one project
dotnet test --project tests/LocalTake.Core.Tests --coverage --coverage-output-format cobertura --coverage-output core.cobertura.xml
python build/check-coverage.py TestResults/core.cobertura.xml 90
dotnet run --project src/LocalTake.Wpf -c Release
dotnet format LocalTake.slnx                          # apply the style; CI runs it with --verify-no-changes
dotnet publish src/LocalTake.Wpf -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Package versions are managed centrally in `Directory.Packages.props`: a `PackageReference` in a project carries no version.

Only one instance can run (named mutex): kill `LocalTake.exe` before rebuilding or running again, otherwise the build cannot overwrite the exe and a new launch exits immediately. Global shortcuts also fail to register while TinyTake or another capture tool holds them.

The UI is verified by hand: launch the app and exercise captures through the shortcuts and the tray menu. To check a recording, `ffmpeg -i file.mp4` lists its streams and `-af volumedetect -f null -` measures its audio. Settings live in `%APPDATA%\LocalTake\settings.json` (deleting it restores defaults), the log in `%LOCALAPPDATA%\LocalTake\logs`.

## Tests

- `tests/LocalTake.Core.Tests`: unit tests of the core, without I/O (MockFileSystem, FakeTimeProvider, fakes in `Fakes.cs`). CI requires 90 % line coverage of the core.
- `tests/LocalTake.Infrastructure.IntegrationTests`: real files and a real ffmpeg, which records its own test sources (`lavfi`) with the production encoding. Tests needing ffmpeg skip themselves when it is not installed; CI installs it.
- `tests/fixtures/legacy`: the behaviour of v0.1.0 (file names, shortcuts, ffmpeg command lines, settings files), recorded from the original code. Characterization tests compare against it; never regenerate it, and make a deliberate change in the test with a comment.
- New behaviour starts with a failing test.

## Versioning and releases

[Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`. The only source of the version is `<Version>` in `Directory.Build.props`.

- While the major version is 0 (initial development), MINOR is for new features and behaviour changes, including breaking ones; PATCH is for fixes only. 1.0.0 is decided by the user.
- What counts as the public interface: the shortcuts, the settings in `settings.json` (an older file must keep loading) and the file names produced.
- To release: bump `<Version>`, commit `Release vX.Y.Z`, tag `vX.Y.Z` on `main`, push the commit and the tag, run the publish command, then `gh release create vX.Y.Z publish/LocalTake.exe` with short notes.

## Architecture

Ports and adapters, in three layers:

- `src/LocalTake.Core` (`net10.0`, no UI framework, no OS API): the rules and use cases. `Capture/` (`CaptureWorkflow`, `CaptureDelivery`, `SelectionModel`), `Recording/` (`RecordingSession`, `RecordingCommand`, `FfmpegLocator`, `DirectShowGdigrab`), `Files/` (`FileNamePattern`, `OutputPathAllocator`, `FileCounter`, `ImageSaver`), `Editor/` (`EditorGeometry`, `UndoHistory`), `Hotkeys/` (`Hotkey`, `KeyCode`), `Settings/` (`AppSettings`, `SettingsDefaults`). Everything platform-specific is an interface in `Ports/`; images are opaque `IImage`.
- `src/LocalTake.Infrastructure` (`net10.0`, portable): `JsonSettingsStore`, the ffmpeg processes (`FfmpegVideoRecorder`, `FfmpegAudioDeviceLister`), `FileLoggerProvider`, `AppPaths`.
- `src/LocalTake.Wpf` (Windows): the UI and the Windows adapters (`Adapters/`, `HotkeyManager`, `WpfRecordingView`, `TrayIcon`). `App.OnStartup` is the composition root (Microsoft.Extensions.DependencyInjection). This project is transitional: an Avalonia app replaces it so that LocalTake also runs on macOS, with the same core.

WPF app (`ShutdownMode=OnExplicitShutdown`, no main window) that also uses WinForms for the tray icon, the selection overlay and the folder picker. `ImplicitUsings` is disabled because the two frameworks share type names; WinForms types are referenced through a `Forms` alias.

Flow: `App` → `TrayController` (menu, global shortcuts) → `CaptureWorkflow` or `RecordingSession`. A capture freezes the whole virtual screen (`WpfScreen`), shows `SelectionOverlay` over that bitmap, then crops it; `CaptureDelivery` then applies `AfterCaptureAction` (open the editor, save, copy, or save and copy).

### Pixel and DPI model (read before touching capture or the editor)

- The process is Per-Monitor-V2 aware (`app.manifest`), so every screen coordinate is in **physical pixels** (`PixelRect`): virtual screen, window bounds (DWM extended frame), overlay, saved region. The WFAC010 warning is suppressed on purpose: a WPF app declares DPI awareness in the manifest.
- `SelectionOverlay` is a borderless WinForms form spanning all monitors in physical pixels. It drops `WM_DPICHANGED` so WinForms never rescales it. Its mouse and keyboard rules are `SelectionModel`'s; the form only draws.
- Every image entering the editor is converted to **96 DPI** (`ScreenCapture.ToBitmapSource` / `Normalize`), so one WPF unit on the editor canvas equals one image pixel. Export renders the canvas through a `VisualBrush` at exactly the image's pixel size, which is independent of zoom and scroll.
- Because the canvas is in physical pixels, sizes chosen in the toolbar (stroke, font, badge, blur block) are multiplied by the display DPI scale (`StrokeWidth`, `TextSize`). Zoom is shown relative to screen pixels.

### Editor (`EditorWindow`)

- Annotations are plain WPF elements on the `Surface` canvas (the first child, `BaseImage`, is the capture). Each one carries a `TranslateTransform`, and moving, cropping and undo all work by changing that offset. Shapes and sizes come from `EditorGeometry`.
- Crop does not flatten: it swaps the base bitmap and shifts every annotation's offset, so annotations remain editable and undo restores both.
- Undo/redo is an `UndoHistory` of `(Undo, Redo)` closures pushed by each operation. Keyboard shortcuts are ignored during a mouse gesture, and Escape or a lost mouse capture cancels the gesture.
- Blur is pixelation: a downscaled `CroppedBitmap` of the base image, upscaled with nearest-neighbour.
- Selection uses an adorner, so it never appears in the exported image.
- A fresh capture counts as unsaved; closing prompts to save. "Quitter" closes editors one by one before `Shutdown`, because cancelling a `Closing` event is ignored during application shutdown.

### Shortcuts

- `Hotkey` (core) parses and formats strings like `Ctrl+Alt+PrintScreen` with a framework-neutral `KeyCode`; `WpfKeyMap` maps it to WPF keys and virtual keys. A key without modifiers is only allowed for Print Screen and the F keys, since a plain key registered system-wide would break it in every other application. `Format(HotkeyNaming.MacOS)` writes Option and Cmd; parsing accepts both namings.
- Global hotkeys use `RegisterHotKey` on a message-only `HwndSource` (`HotkeyManager`); actions are dispatched asynchronously because captures open modal windows.
- Hotkeys are unregistered while the settings window is open, so pressing one reaches the input box. Print Screen only produces a key-up event, which the box handles separately.
- `SettingsDefaults.ForWindows` holds the Print Screen shortcuts of v0.1.0; `ForMacOS` uses Ctrl+Shift with digits, since a Mac has no Print Screen and Cmd+Shift+3/4/5 belong to macOS.

### Video (`RecordingSession`, `Recording/`)

- Recording shells out to an external ffmpeg (setting, then next to the exe, then PATH, then Homebrew's folders on macOS). LocalTake never downloads it: that would be a network call.
- `IFfmpegPlatform` gives each OS its inputs; `RecordingCommand` adds the mapping and the encoding, common to all. On Windows (`DirectShowGdigrab`), `gdigrab` takes the same physical virtual-desktop coordinates as the rest of the app. Width and height are rounded down to even values for H.264/yuv420p (`PixelRect.WithEvenSize`).
- Nothing said while the counter runs may be cut. Start: the devices take about 0.3 s to open, so the bar shows "Démarrage…" until ffmpeg prints "Press [q] to stop" (read through `-loglevel level+info`; the log keeps only warnings and errors), and only then starts the counter. End: x264 runs with `-tune zerolatency`; with frame threads it held about 0.35 s of frames, ffmpeg read the audio that far behind, and `q` discarded it. The integration test `Stop_keeps_the_end_of_the_sound` compares the end of each stream.
- Stop sends `q` on ffmpeg's stdin, which writes the MP4 index. Killing the process leaves an unplayable file, so kill is only for cancel and failure, and the file is then deleted.
- `RecordingFrame` (WinForms, click-through) and `RecordingBar` (WPF) use `WDA_EXCLUDEFROMCAPTURE`: they are on screen but absent from the video, even in full screen.
- `RecordingSession` is a state machine (Idle, Selecting, Ready, Countdown, Recording, Finishing) whose countdown runs on a `TimeProvider`. After the selection, the bar waits in Ready, as in TinyTake: the countdown only starts from its Start button or the Start shortcut. Image captures are refused while it is busy (`IRecordingStatus`), since the overlay or editor would be recorded.
- The Start and Stop shortcuts (`Ctrl+R`, `Ctrl+D`) are registered on a separate `IGlobalHotkeys` only from Ready until the session ends, so they do not steal those keys from other applications the rest of the time. Stop during the countdown returns to Ready.
- A saved video opens in `VideoPreviewWindow`, as in TinyTake, instead of a balloon: it plays at once, with a seek bar that previews frames on hover through a second muted `MediaElement`. Both `MediaElement`s keep the file open, so Delete closes them and waits for the lock to drop before sending the file to the Recycle Bin. Previews pause as soon as a new video session starts, so that their sound is not recorded. "Refaire" discards the video without confirmation (it stays in the Recycle Bin) and records again the same way (`VideoSavedEventArgs.FullScreen`); an area is selected again. It is refused while another session is under way, since `ToggleAsync` would act on that session.
- Audio uses DirectShow devices. "Computer sound" relies on `virtual-audio-capturer`, the driver installed by TinyTake Filter; with a microphone too, the two are mixed with `amix`.
- The bar's two toggles write `RecordMicrophone` and `RecordSystemAudio` directly to the settings, so the last choice is remembered. An empty `Microphone` means the first one ffmpeg lists. Devices are listed while the bar is shown; the toggles lock once the countdown starts.

### Settings and file names

- `ISettingsStore.Save` throws; `TrySave` swallows IO errors and is used for incidental state (last region, counter, editor preferences) so a locked file never interrupts a capture.
- `JsonSettingsStore` writes the keys of v0.1.0 plus `SchemaVersion`; a key missing from the file keeps the platform default (`SettingsDefaults`), and a corrupt file gives the defaults.
- File names come from `FileNamePattern.Expand`, which is side-effect free and shared with the settings preview; videos use the same pattern via `OutputPathAllocator`. Invalid characters are those of Windows, on every platform, so a pattern names files alike everywhere. The `%NUM%` counter only advances after a successful save (`FileCounter`).
