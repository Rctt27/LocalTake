# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

LocalTake is a tray (Windows) and menu bar (macOS) screen-capture and screen-recording tool, modelled on TinyTake, with every remote feature removed. **Captures must never leave the machine**: no network calls, no upload, no telemetry. This is also why the default Windows output folder avoids a OneDrive-synced Pictures folder (`AppPaths.WindowsOutputFolder`), why ffmpeg is never downloaded, and why the log is a local file only.

UI strings are French; code and comments are English.

## Commands

Requires the .NET 10 SDK (pinned in `global.json`). Analyzers run at `latest-recommended` with warnings as errors; `.editorconfig` holds the style, and CI rejects unformatted code.

```
dotnet build LocalTake.slnx -c Release
dotnet test --solution LocalTake.slnx                 # every test project (Microsoft.Testing.Platform, xunit v3)
dotnet test --project tests/LocalTake.Core.Tests      # one project
dotnet test --project tests/LocalTake.Core.Tests --coverage --coverage-output-format cobertura --coverage-output core.cobertura.xml
python build/check-coverage.py TestResults/core.cobertura.xml 90
dotnet run --project src/LocalTake.Windows            # the app; add "-- --diagnostics" for the self-check report
dotnet format LocalTake.slnx                          # apply the style
dotnet publish src/LocalTake.Windows -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

The macOS app builds on a Mac only, with Xcode and the `macos` workload; `LocalTake.Mac.slnx` holds it with the projects it uses. Each .NET for macOS requires one Xcode: CI pins workload set `10.0.401` (.NET for macOS 26.5) with Xcode 26.6, in `ci.yml` and `release.yml` alike; change both together.

```
dotnet workload install macos --version 10.0.401
dotnet build src/LocalTake.Mac                        # bin/Release/net10.0-macos/LocalTake.app, universal (arm64 and x86_64)
open -a LocalTake --args --diagnostics             # report in ~/Library/Logs/LocalTake/diagnostics.txt
```

Run from a shell (`Contents/MacOS/LocalTake`), the app would be checked with the shell's privacy permissions, not its own.

Package versions are managed centrally in `Directory.Packages.props`: a `PackageReference` in a project carries no version.

Only one instance can run (a named mutex on Windows, the bundle identifier on macOS): kill `LocalTake` before running again, otherwise a new launch exits immediately. Global shortcuts fail to register while TinyTake or another capture tool holds them.

The UI on a real screen is verified by hand with `docs/test-checklist.md`. To check a recording, `ffmpeg -i file.mp4` lists its streams and `-af volumedetect -f null -` measures its audio. Settings and log locations are in README.md.

## Tests

- `tests/LocalTake.Core.Tests`: unit tests of the core, without I/O (MockFileSystem, FakeTimeProvider, fakes in `Fakes.cs`). CI requires 90 % line coverage of the core.
- `tests/LocalTake.Infrastructure.IntegrationTests`: real files and a real ffmpeg, which records its own test sources (`lavfi`) with the production encoding. Tests needing ffmpeg skip themselves when it is not installed; CI installs it.
- `tests/LocalTake.Platform.Tests`: each platform's adapters, run on that platform (CI runs Windows and macOS); tables, files and the macOS screen geometry (`MacScreenSpace`) are checked everywhere.
- The macOS app has no automated tests (no reliable `net10.0-macos` test host in CI): logic worth a test goes to the core or `LocalTake.Platform.MacOS`, and the app is checked by hand with the macOS part of `docs/test-checklist.md`, using the CI artifact `LocalTake-macos`.
- `tests/LocalTake.Windows.Tests` (Windows only): the WPF adapters and windows, off screen on one STA thread hosting `App` without starting it (`Sta.Run`); the editor's export is checked pixel by pixel.
- `tests/fixtures/legacy`: the behaviour of v0.1.0 (file names, shortcuts, ffmpeg command lines, settings files), recorded from the original code. Characterization tests compare against it; never regenerate it, and make a deliberate change in the test with a comment.
- New behaviour starts with a failing test.

## Versioning and releases

[Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`. The only source of the version is `<Version>` in `Directory.Build.props`.

- While the major version is 0 (initial development), MINOR is for new features and behaviour changes, including breaking ones; PATCH is for fixes only. 1.0.0 is decided by the user.
- What counts as the public interface: the shortcuts, the settings in `settings.json` (an older file must keep loading) and the file names produced.
- To release: bump `<Version>`, commit `Release vX.Y.Z`, tag `vX.Y.Z` on `main` and push both. `.github/workflows/release.yml` builds the Windows exe and the macOS app (zipped, signed ad hoc, neither notarized nor signed with a Developer ID) and creates the GitHub release; it refuses a tag that does not match `<Version>`.

## Architecture

A shared core, and one native app per operating system. Keep every rule that can be tested in the core, so that each app stays views and adapters.

- `src/LocalTake.Core` (`net10.0`, no UI framework, no OS API): the rules and use cases. `Capture/` (`CaptureWorkflow`, `CaptureDelivery`, `SelectionModel`), `Recording/` (`RecordingSession`, `RecordingCommand`, `FfmpegLocator`, and the ffmpeg inputs of each OS: `DirectShowGdigrab`, `AvFoundation`), `Files/` (`FileNamePattern`, `OutputPathAllocator`, `FileCounter`, `ImageSaver`), `Editor/` (`EditorGeometry`, `UndoHistory`), `Hotkeys/` (`Hotkey`, `KeyCode`), `Settings/` (`AppSettings`, `SettingsDefaults`, `SettingsEditor`). Everything platform-specific is an interface in `Ports/`; images cross ports as opaque `IImage` or as `PixelBuffer` (BGRA, straight alpha), which also crops and pixelates.
- `src/LocalTake.Infrastructure` (portable): `JsonSettingsStore`, the ffmpeg processes (`FfmpegVideoRecorder`, `FfmpegAudioDeviceLister`), `FileLoggerProvider`, `AppPaths`, and `DiagnosticsReport` (`--diagnostics`).
- `src/LocalTake.Platform.Windows`: the Win32 ports in `LibraryImport` (shortcuts, screen grab and window list, clipboard, Recycle Bin, Run key, Explorer, window display affinity). `src/LocalTake.Platform.MacOS` (`net10.0`, tested on every runner): what Apple's bindings do not cover, Carbon shortcuts and key codes, the window list, and the screens' geometry (`MacScreenSpace`).
- `src/LocalTake.Windows` (WPF, `net10.0-windows`, `AssemblyName` LocalTake): the views and the WPF adapters (`Adapters/`). `App.ConfigureServices` is the composition root. WinForms is used for `NotifyIcon` and for the overlay and frame windows.
- `src/LocalTake.Mac` (`net10.0-macos`, AppKit and AVKit, `LSUIElement`: no Dock icon): `StatusMenu` (the menu bar icon, as `TrayController`), `Capture/` (one overlay window per screen), `Editor/`, `Recording/`, `Video/` (an `AVPlayerView`), `Settings/`, and the adapters on Apple's APIs (`Platform/`: ScreenCaptureKit, NSPasteboard, Trash, `SMAppService`; `Adapters/`). `Composition.Build` is the composition root. A main menu, invisible in a menu bar app, gives the editor ⌘S, ⌘Z, ⌘C and ⌘Q through the responder chain.

Flow: `App` → `TrayController` (tray menu, global shortcuts) → `CaptureWorkflow` or `RecordingSession`. A capture freezes the virtual desktop (`IScreenGrabber`), shows `SelectionOverlay`, then crops the frozen pixels; `CaptureDelivery` applies `AfterCaptureAction` (open the editor, save, copy, or save and copy).

### Pixel and DPI model (read before touching capture or the editor)

- Every screen coordinate is in **physical pixels** (`PixelRect`): virtual desktop, monitors, windows, overlay input, saved region. The process is Per-Monitor-V2 aware (`app.manifest`).
- `SelectionOverlay` is one borderless form over the whole virtual desktop, drawing the frozen screenshot; its rules are `SelectionModel`'s.
- The editor canvas is sized in image pixels (bitmaps at 96 DPI, so one WPF unit is one pixel). Export (`EditorWindow.RenderImage`) renders the canvas through a `VisualBrush` at exactly the image's pixel size, independent of zoom and scroll. Sizes chosen in the toolbar (stroke, font, badge, blur block) are multiplied by the monitor's DPI scale; zoom is shown relative to screen pixels.

### macOS screens

- macOS works in points, with Cocoa's origin at the bottom left of the main screen; screens may have different scales. `MacScreenSpace.Layout` lays them out in pixels, origin at the top left of the main screen: origins scaled by the largest scale, sizes by each screen's own, so each keeps its full resolution and none overlap (gaps between them stay black in a capture). `ToPixel`, `ToCocoa` and `FromCocoa` convert; `FromTopLeftPoints` converts Quartz window frames.
- A capture takes one ScreenCaptureKit screenshot per screen and assembles them (`MacScreenSpace.Compose`). Without the Screen Recording permission it throws a message saying where to grant it. The window list comes from `CGWindowListCopyWindowInfo`, the only one ordered front to back.
- A selection stays on the screen where it started. ffmpeg numbers screens in `CGGetActiveDisplayList`'s order, which `MacDisplays` (`IDisplays`, printed by `--diagnostics`) follows; it need not be `NSScreen.Screens`'.
- Shortcuts are registered exclusively, and those macOS uses itself (`CopySymbolicHotKeys`, Cmd+Shift+3…) are refused, so a taken shortcut is reported rather than silent.

### Editor (`EditorWindow`)

- Annotations are plain WPF elements on the `Surface` canvas (the first child is the capture). Each carries a `TranslateTransform`; moving, cropping and undo change that offset. Shapes and sizes come from `EditorGeometry`.
- Crop does not flatten: it swaps the base image and shifts every annotation, so annotations stay editable and undo restores both.
- Undo/redo is an `UndoHistory` of `(Undo, Redo)` closures. Keyboard shortcuts are ignored during a gesture; Escape or a lost mouse capture cancels it.
- Blur is `PixelBuffer.Pixelate` of the base image, placed as an image annotation.
- A fresh capture counts as unsaved; closing asks to save.

### Shortcuts

- `Hotkey` (core) parses and formats strings like `Ctrl+Alt+PrintScreen` with a framework-neutral `KeyCode`. A key without modifiers is only allowed for Print Screen and the F keys, since a plain key registered system-wide would break it in every other application. `Format(HotkeyNaming.MacOS)` writes Option and Cmd; parsing accepts both namings.
- Windows: `RegisterHotKey` on a message-only window (`WindowsHotkeys`). For the macOS app: Carbon `RegisterEventHotKey` (`MacHotkeys`), which needs no Accessibility permission; a Mac has no Print Screen, so `SettingsDefaults.ForMacOS` uses Ctrl+Shift with digits (Cmd+Shift+3/4/5 belong to macOS).
- Actions run through `IUiThread.Post`, outside the input handling, because captures open windows. Shortcuts are unregistered while the settings window is open, so pressing one reaches the input box. Print Screen only produces a key-up event on Windows, which the box handles separately.

### Video (`RecordingSession`, `Recording/`)

- Recording shells out to an external ffmpeg (setting, then next to the app, then PATH, then Homebrew's folders on macOS, since an app started from the Finder does not get the shell's PATH).
- `IFfmpegPlatform` gives each OS its inputs; `RecordingCommand` adds the mapping and the encoding, common to all. Windows (`DirectShowGdigrab`): `gdigrab` takes physical virtual-desktop coordinates, DirectShow the sound. macOS (`AvFoundation`): a whole display is recorded and cropped to the area (`-vf crop`); an area across two displays keeps the part on the display holding most of it. Width and height are even for H.264/yuv420p (`PixelRect.WithEvenSize`).
- Nothing said while the counter runs may be cut. Start: the devices take about 0.3 s to open, so the bar shows "Démarrage…" until ffmpeg prints "Press [q] to stop" (read through `-loglevel level+info`), and only then starts the counter. End: x264 runs with `-tune zerolatency`; with frame threads it held about 0.35 s of frames and `q` discarded the matching audio. The integration test `Stop_keeps_the_end_of_the_sound` guards this.
- Stop sends `q` on ffmpeg's stdin, which writes the MP4 index. Killing the process leaves an unplayable file, so kill is only for cancel and failure, and the file is then deleted.
- The recording frame (`RecordingFrame`) is a click-through layered window drawn just outside the area; it and the bar are excluded from captures (`WDA_EXCLUDEFROMCAPTURE`), so neither can appear in the video.
- `RecordingSession` is a state machine (Idle, Selecting, Ready, Countdown, Recording, Finishing) whose countdown runs on a `TimeProvider`. After the selection, the bar waits in Ready, as in TinyTake. Image captures are refused while it is busy (`IRecordingStatus`). The Start and Stop shortcuts are registered on a separate `IGlobalHotkeys` only from Ready until the session ends; Stop during the countdown returns to Ready.
- A saved video opens in `VideoPreviewWindow` and plays there (`MediaElement`), with a frame preview on hover over the seek bar from a second, paused `MediaElement`. Both release the file before "Supprimer" sends it to the Recycle Bin. "Refaire" sends the video to the trash and records again the same way (`VideoSavedEventArgs.FullScreen`); it is refused while another session is under way.
- "Computer sound" needs a virtual audio driver: `virtual-audio-capturer` (TinyTake Filter) on Windows, BlackHole on macOS (`IFfmpegPlatform.SystemAudioDevice`); with a microphone too, the two are mixed with `amix`.
- The bar's two toggles write `RecordMicrophone` and `RecordSystemAudio` directly to the settings, so the last choice is remembered. An empty `Microphone` means the first one ffmpeg lists.

### Settings and file names

- `ISettingsStore.Save` throws; `TrySave` swallows IO errors and is used for incidental state (last region, counter, editor preferences) so a locked file never interrupts a capture.
- `JsonSettingsStore` writes the keys of v0.1.0 plus `SchemaVersion`; a key missing from the file keeps the platform default (`SettingsDefaults`), and a corrupt file gives the defaults.
- The settings window's checks and saving are `SettingsEditor.TryApply` (core); the window fills a `SettingsForm` and shows the message it returns.
- File names come from `FileNamePattern.Expand`, side-effect free and shared with the settings preview; videos use the same pattern. Invalid characters are those of Windows, on every platform, so a pattern names files alike everywhere. The `%NUM%` counter only advances after a successful save (`FileCounter`).
