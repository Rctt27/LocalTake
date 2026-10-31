# Son de l'ordinateur via WASAPI (0.2.0) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sur Windows, enregistrer le son de l'ordinateur sans pilote audio virtuel (`virtual-audio-capturer`) : ShowTime le capte en loopback WASAPI et le donne à ffmpeg par un named pipe.

**Architecture:** Report sur l'architecture 0.2.0 de la branche `feature/wasapi-system-audio`, construite par erreur sur la v0.1.0. Ce qui ne dépend d'aucun OS va dans `ShowTime.Core` : format brut, ordre des entrées, arguments. La capture WASAPI va dans `ShowTime.Platform.Windows`, derrière un port du cœur. L'enregistreur ffmpeg d'`Infrastructure` possède la capture pendant l'enregistrement. macOS ne change pas (BlackHole).

**Tech Stack:** .NET 10, NAudio.Wasapi 2.2.1 (version centrale), xunit v3, Shouldly, NSubstitute.

**Spec:** la conversation du 2026-10-20 et le plan v1 (`git show feature/wasapi-system-audio:docs/superpowers/plans/2026-10-20-wasapi-system-audio.md`). Son code et ses mesures sont la référence : `git show feature/wasapi-system-audio:<fichier>`.

## Décisions reprises de la v1 (mesurées)

- La capture ne démarre qu'à la connexion d'ffmpeg au pipe. Un silence est joué sur la même sortie. Les zéros sont écrits en temps réel si la sortie est perdue.
- **Ordre des entrées : micro, écran, puis pipe**, avec `-probesize 32 -analyzeduration 0` sur le pipe et, quand le son de l'ordinateur est enregistré, sur gdigrab. Écart mesuré entre un flash et un bip : +0,82 s avec le pipe avant l'écran, −0,6 s sans limiter le sondage de gdigrab, −0,02 à −0,06 s ainsi.
- `-ac 2` en sortie dès que le son de l'ordinateur est enregistré.

## Corrections issues de la relecture de la v1

- **Arrêt :** si ffmpeg n'est pas sorti 3 s après `q`, l'enregistreur libère la capture. La fermeture du pipe donne EOF à ffmpeg, qui finalise le fichier. Cela évite 30 s d'attente et une vidéo perdue quand le pipe reste vide.
- **Démarrage :** tout échec de `Play`/`StartRecording` passe au remplissage de zéros. Rien ne démarre si la capture est déjà libérée.
- **Objets NAudio construits sur un thread du pool** (sans `SynchronizationContext`) : l'app et les tests suivent le même chemin, et `RecordingStopped` arrive sur le thread de capture, sans délai avant le remplissage.
- **Silence facultatif :** si `WasapiOut` ne peut pas s'ouvrir (sortie en mode exclusif), l'enregistrement continue sans lui.
- La `CancellationTokenSource` est libérée. Le commentaire de `Dispose` dit qu'il attend la fin des threads NAudio.

## Global Constraints

- Aucun appel réseau. Interface en français, code et commentaires en anglais.
- Analyseurs `latest-recommended`, avertissements en erreurs, `dotnet format ShowTime.slnx --verify-no-changes` propre. Versions de paquets dans `Directory.Packages.props`.
- Couverture du cœur ≥ 90 % (`build/check-coverage.py`).
- `tests/fixtures/legacy` n'est jamais régénéré. Les cas `system-audio` et `both` de `ffmpeg-arguments.json` changent volontairement : le test le dit dans un commentaire.
- `RecordSystemAudio` garde son nom et son sens. Ne pas toucher `<Version>` (prochaine version : 0.3.0, décidée par Renaud).

## Review Focus

1. **Le pipe ne reçoit rien** (capture morte) → Arrêter finalise quand même une vidéo lisible en quelques secondes. *Task 3 `Stop_finishes_the_file_when_the_computer_sound_never_comes`.*
2. **Rien ne joue de tout l'enregistrement** → la piste dure autant que la vidéo. *Task 4 `SyncTests` et vérification manuelle.*
3. **ffmpeg ne se connecte jamais** → la libération est immédiate. *Task 2 `Dispose_without_ffmpeg_returns_promptly`.*
4. **Pas de sortie audio** (CI, machine sans carte son) → « Son du PC » est indisponible et les tests Device sont ignorés, pas en échec. *Task 1 `CanRecordSystemAudio` ; tests avec `Assert.SkipUnless`.*
5. **Sortie en 44,1 kHz ou en 5.1** → le vrai format est déclaré et la sortie est en stéréo. *Task 1 `Pipe_declares_the_device_format_and_downmixes`.*

---

### Task 1: Cœur — format brut, ordre des entrées, arguments

**Files:**
- Create: `src/ShowTime.Core/Recording/PipedAudio.cs` (`RawAudioFormat`, `PipedAudio`, `SilencePadding`)
- Modify: `CaptureInputs.cs` (`TrailingAudioInputs`), `RecordingCommand.cs`, `IFfmpegPlatform.cs`, `DirectShowGdigrab.cs`, `AvFoundation.cs`
- Test: `tests/ShowTime.Core.Tests/Recording/PipedAudioTests.cs`, `RecordingCommandTests.cs`, `DirectShowDeviceListTests.cs`, `AvFoundationTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record RawAudioFormat(string SampleFormat, int SampleRate, int Channels)` avec `int BlockAlign` et `static RawAudioFormat From(bool isFloat, int bitsPerSample, int sampleRate, int channels)` (`NotSupportedException` pour le reste) ; `public sealed record PipedAudio(string PipePath, RawAudioFormat Format)` ; `public static class SilencePadding { long FramesDue(TimeSpan elapsed, long framesWritten, int sampleRate) }`.
  - `CaptureInputs(..., IReadOnlyList<IReadOnlyList<string>>? TrailingAudioInputs = null)` : des entrées audio ouvertes après l'écran.
  - `IFfmpegPlatform.BuildInputs(PixelRect area, VideoOptions options, PipedAudio? systemAudio)` et `bool CanRecordSystemAudio(IReadOnlyCollection<string> devices)`.
  - `DirectShowGdigrab(Func<bool> hasAudioOutput)` : `SystemAudioDevice` reste `virtual-audio-capturer`, seulement pour l'exclure des micros. `SystemAudioRequirement` = « une sortie audio (haut-parleurs ou casque) ».

- [ ] Tests d'abord :
  - `RawAudioFormat`/`SilencePadding` : mêmes cas que la v1.
  - `RecordingCommand` place les `TrailingAudioInputs` après l'écran et calcule `-map` et `amix` sur les vrais indices.
  - `DirectShowGdigrab` avec pipe : son seul (`-map 0:v -map 1:a`), micro et pipe (`[0:a][2:a]amix…`, `-map 1:v`), 44,1 kHz/5.1 avec `-ac 2` en sortie, sondage limité sur gdigrab et le pipe.
  - `SystemAudio` sans pipe → `ArgumentException`.
  - `CanRecordSystemAudio` vaut `hasAudioOutput()` sous Windows, et pour AvFoundation `devices.Count == 0 || devices.Contains("BlackHole 2ch")`.
  - Cas legacy `system-audio`/`both` : comparés à v0.1.0 sauf pour le son de l'ordinateur, avec un commentaire.
- [ ] Les voir échouer, implémenter, `dotnet test --project tests/ShowTime.Core.Tests` vert, couverture ≥ 90 %, commit.

### Task 2: Port et capture WASAPI

**Files:**
- Modify: `src/ShowTime.Core/Ports/IRecordingPorts.cs` (`ISystemAudioSource`, `ISystemAudioCapture`)
- Create: `src/ShowTime.Platform.Windows/WasapiSystemAudio.cs` ; package NAudio.Wasapi dans `Directory.Packages.props` et le projet
- Test: `tests/ShowTime.Platform.Tests/Windows/WasapiSystemAudioTests.cs`

**Interfaces:**
- Produces: `public interface ISystemAudioSource { bool IsAvailable(); ISystemAudioCapture Start(); }` ; `public interface ISystemAudioCapture : IDisposable { PipedAudio Input { get; } }` ; `public sealed class WasapiSystemAudio : ISystemAudioSource`. Le nom du pipe se lit dans `Input.PipePath`.

- [ ] Tests Device (`Assert.SkipUnless(OperatingSystem.IsWindows() && source.IsAvailable(), …)`) :
  - `Capture_delivers_audio_at_its_declared_rate` ;
  - `Dispose_without_ffmpeg_returns_promptly` ;
  - `Disconnect_does_not_throw` ;
  - `Start_off_the_UI_thread_has_no_synchronization_context`, qui démarre depuis un thread STA avec un `SynchronizationContext` posé et vérifie que les données arrivent.
- [ ] Implémentation reprise de la v1 avec les corrections ci-dessus. Commit.

### Task 3: L'enregistreur possède la capture

**Files:**
- Modify: `src/ShowTime.Infrastructure/Recording/FfmpegVideoRecorderFactory.cs`, `FfmpegVideoRecorder.cs`
- Test: `tests/ShowTime.Infrastructure.IntegrationTests/Recording/FfmpegRecorderTests.cs`

**Interfaces:**
- Consumes: `ISystemAudioSource`, `ISystemAudioCapture`, `IFfmpegPlatform.BuildInputs(…, PipedAudio?)`.
- Produces: `FfmpegVideoRecorderFactory(IFfmpegPlatform platform, ISystemAudioSource? systemAudio = null)` ; `FfmpegVideoRecorder.Start(string ffmpegPath, CaptureInputs inputs, string outputPath, Action<IVideoRecorder> onUnexpectedExit, IDisposable? systemAudio = null)`.

- [ ] Tests (ffmpeg réel, sources lavfi pour l'image) :
  - `Stop_finishes_the_file_when_the_computer_sound_never_comes` : un pipe serveur qui accepte sans rien écrire. Arrêter rend `true` en moins de 10 s et le fichier se lit.
  - `Piped_raw_audio_is_recorded` : le test écrit un sinus `f32le` dans le pipe, la vidéo contient `Audio: aac`.
  - `The_capture_is_freed_once_ffmpeg_exits` et `Kill_frees_the_capture`, avec un faux `IDisposable`.
- [ ] Factory : si `options.SystemAudio` et une source existe, démarrer la capture avant ffmpeg et la libérer si le lancement échoue. Enregistreur : `q`, puis 3 s, puis libérer la capture, puis attendre jusqu'à 30 s ; libérer aussi dans `Kill`/`Dispose`. Commit.

### Task 4: Session, interface, diagnostic, composition, docs

**Files:**
- Modify: `RecordingSession.cs` (`platform.CanRecordSystemAudio(devices)`, message « L'enregistrement n'a pas pu démarrer : »), `RecordingBar.xaml.cs`, `SettingsWindow.xaml.cs`, `DiagnosticsReport.cs`, `App.xaml.cs` (DI), `README.md`, `CLAUDE.md`, `docs/test-checklist.md`
- Test: `RecordingSessionTests.cs`, `DiagnosticsReportTests.cs`, `tests/ShowTime.Windows.Tests/Recording/SyncTests.cs` (repris de la v1, Device)

- [ ] Tests d'abord :
  - la session suit `CanRecordSystemAudio` ;
  - le diagnostic « computer sound » rapporte la sortie audio sous Windows ;
  - `SyncTests` (flash et bip, ±0,1 s) passe avec la composition réelle (`DirectShowGdigrab` et `WasapiSystemAudio`).
- [ ] Barre et réglages :
  - disponibilité par `CanRecordSystemAudio`, calculée dès l'ouverture et non plus seulement après la liste ffmpeg ;
  - infobulles « Aucune sortie audio détectée sur cet ordinateur. » et « Enregistre ce que joue la sortie audio par défaut. »
- [ ] DI : `ISystemAudioSource` → `WasapiSystemAudio` ; `IFfmpegPlatform` → `new DirectShowGdigrab(source.IsAvailable)`.
- [ ] Docs :
  - README : la phrase sur le son de l'ordinateur ;
  - CLAUDE.md : section Video ;
  - checklist : débrancher un casque, Annuler pendant « Démarrage… ».
- [ ] Vérification : `dotnet build ShowTime.slnx -c Release`, `dotnet test --solution ShowTime.slnx`, `dotnet format --verify-no-changes`. Commit.
