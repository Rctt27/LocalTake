# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

LocalTake is a Windows tray screen-capture and screen-recording tool modelled on TinyTake, with every remote feature removed. **Captures must never leave the machine**: no network calls, no upload, no telemetry. This is also why the default output folder avoids a OneDrive-synced Pictures folder (`AppSettings.DefaultOutputFolder`).

UI strings are French; code and comments are English.

## Commands

Requires the .NET 8 SDK. There is no test project and no linter configured.

```
dotnet build -c Release
dotnet run -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Only one instance can run (named mutex): kill `LocalTake.exe` before rebuilding or running again, otherwise the build cannot overwrite the exe and a new launch exits immediately. Global shortcuts also fail to register while TinyTake or another capture tool holds them.

Verification is manual: launch the app and exercise captures through the shortcuts and the tray menu. To check a recording, `ffmpeg -i file.mp4` lists its streams and `-af volumedetect -f null -` measures its audio. Settings live in `%APPDATA%\LocalTake\settings.json`; deleting it restores defaults.

## Architecture

WPF app (`ShutdownMode=OnExplicitShutdown`, no main window) that also uses WinForms for the tray icon, the selection overlay and the folder picker. `ImplicitUsings` is disabled because the two frameworks share type names; WinForms types are referenced through a `Forms` alias.

Flow: `App` → `TrayController` owns the `NotifyIcon`, the `HotkeyManager` and what happens after a capture (`AfterCaptureAction`: open the editor, save, copy, or save and copy). A capture freezes the whole virtual screen (`ScreenCapture`), shows `SelectionOverlay` over that bitmap, then crops it.

### Pixel and DPI model (read before touching capture or the editor)

- The process is Per-Monitor-V2 aware (`app.manifest`), so every screen coordinate is in **physical pixels**: virtual screen, window bounds (DWM extended frame), overlay, saved region. The WFAC010 warning is suppressed on purpose: a WPF app declares DPI awareness in the manifest.
- `SelectionOverlay` is a borderless WinForms form spanning all monitors in physical pixels. It drops `WM_DPICHANGED` so WinForms never rescales it.
- Every image entering the editor is converted to **96 DPI** (`ScreenCapture.ToBitmapSource` / `Normalize`), so one WPF unit on the editor canvas equals one image pixel. Export renders the canvas through a `VisualBrush` at exactly the image's pixel size, which is independent of zoom and scroll.
- Because the canvas is in physical pixels, sizes chosen in the toolbar (stroke, font, badge, blur block) are multiplied by the display DPI scale (`StrokeWidth`, `TextSize`). Zoom is shown relative to screen pixels.

### Editor (`EditorWindow`)

- Annotations are plain WPF elements on the `Surface` canvas (the first child, `BaseImage`, is the capture). Each one carries a `TranslateTransform`, and moving, cropping and undo all work by changing that offset.
- Crop does not flatten: it swaps the base bitmap and shifts every annotation's offset, so annotations remain editable and undo restores both.
- Undo/redo is a stack of `(Undo, Redo)` closures pushed by each operation. Keyboard shortcuts are ignored during a mouse gesture, and Escape or a lost mouse capture cancels the gesture.
- Blur is pixelation: a downscaled `CroppedBitmap` of the base image, upscaled with nearest-neighbour.
- Selection uses an adorner, so it never appears in the exported image.
- A fresh capture counts as unsaved; closing prompts to save. "Quitter" closes editors one by one before `Shutdown`, because cancelling a `Closing` event is ignored during application shutdown.

### Shortcuts

- Global hotkeys use `RegisterHotKey` on a message-only `HwndSource`; actions are dispatched asynchronously because captures open modal windows.
- `Hotkey` parses and formats strings like `Ctrl+Alt+PrintScreen`. A key without modifiers is only allowed for Print Screen and the F keys, since a plain key registered system-wide would break it in every other application.
- Hotkeys are unregistered while the settings window is open, so pressing one reaches the input box. Print Screen only produces a key-up event, which the box handles separately.

### Video (`VideoController`, `Ffmpeg.cs`)

- Recording shells out to an external ffmpeg (setting, then next to the exe, then PATH). LocalTake never downloads it: that would be a network call.
- ffmpeg `gdigrab` takes the same physical virtual-desktop coordinates as the rest of the app. Width and height are rounded down to even values for H.264/yuv420p.
- Stop sends `q` on ffmpeg's stdin, which writes the MP4 index. Killing the process leaves an unplayable file, so kill is only for cancel and failure, and the file is then deleted.
- `RecordingFrame` (WinForms, click-through) and `RecordingBar` (WPF) use `WDA_EXCLUDEFROMCAPTURE`: they are on screen but absent from the video, even in full screen.
- `VideoController` is a state machine (Idle, Selecting, Ready, Countdown, Recording, Finishing). After the selection, the bar waits in Ready, as in TinyTake: the countdown only starts from its Start button or the Start shortcut. Image captures are refused while it is busy, since the overlay or editor would be recorded.
- The Start and Stop shortcuts (`Ctrl+R`, `Ctrl+D`) are registered on a separate `HotkeyManager` only from Ready until the session ends, so they do not steal those keys from other applications the rest of the time. Stop during the countdown returns to Ready.
- Audio uses DirectShow devices. "Computer sound" relies on `virtual-audio-capturer`, the driver installed by TinyTake Filter; with a microphone too, the two are mixed with `amix`.
- The bar's two toggles write `RecordMicrophone` and `RecordSystemAudio` directly to the settings, so the last choice is remembered. An empty `Microphone` means the first one ffmpeg lists. Devices are listed while the bar is shown; the toggles lock once the countdown starts.

### Settings and file names

- `AppSettings.Save()` throws; `TrySave()` swallows IO errors and is used for incidental state (last region, counter, editor preferences) so a locked file never interrupts a capture.
- File names come from `ImageExporter.ExpandPattern`, which is side-effect free and shared with the settings preview; videos use the same pattern via `NextOutputPath`. The `%NUM%` counter only advances after a successful save.
