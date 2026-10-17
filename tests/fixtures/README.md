# Test fixtures

`legacy/` holds the behaviour of LocalTake v0.1.0, recorded by calling the original WPF code before it was split
into `LocalTake.Core`. The characterization tests compare the core against these files, so that file names,
shortcuts, ffmpeg arguments and settings files keep working as they did.

- `expand-pattern.json`: file name pattern, counter and the name produced (date 2026-03-07 09:05:02).
- `hotkeys.json`: shortcut text, whether it parsed, and how it was written back.
- `ffmpeg-arguments.json`: recording area and options, and the ffmpeg command line.
- `pick-microphone.json`: listed devices, preferred microphone and the one used.
- `settings-v0.1.0.json`, `settings-v0.1.0-defaults.json`: settings files as v0.1.0 writes them. An older file
  must keep loading (see CLAUDE.md, "Versioning and releases").

Do not regenerate them: they are the reference. A deliberate behaviour change is made in the test, with a comment.
