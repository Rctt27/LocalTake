# Son du Mac sans pilote — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** sur Mac, « Son du Mac » enregistre ce que joue l'ordinateur sans BlackHole : ShowTime le capte avec ScreenCaptureKit et le passe à ffmpeg par un tuyau (FIFO), comme `WasapiSystemAudio` sous Windows.

**Architecture:** même schéma que Windows (`ISystemAudioSource` → `ISystemAudioCapture` → `PipedAudio`, entrée ffmpeg ouverte après l'écran). La logique testable est dans le cœur (`AudioTimeline` : placer chaque paquet de son à sa date, combler par du silence ; `PlanarAudio` : entrelacer les canaux) et dans `ShowTime.Platform.MacOS` (`MacAudioPipe` : FIFO, attente de ffmpeg, écriture, comblement). L'app Mac n'ajoute que la capture ScreenCaptureKit (`MacSystemAudio`) et son branchement.

**Tech Stack:** .NET 10, C#, ScreenCaptureKit / CoreMedia (liaisons .NET pour macOS), libc `mkfifo` en `LibraryImport`, ffmpeg 9.0.2 livré, xunit v3 + Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-24-macos-parity-design.md` (objectif 3). Plan 2 sur 4 (le plan 1, livraison sans dépendance, est terminé).

## Global Constraints

- Branche `feature/macos-app` ; commits terminés par les lignes d'attribution de la session.
- Tests en local : l'utilisateur autorise `tests/ShowTime.Core.Tests` et `tests/ShowTime.Infrastructure.IntegrationTests`. `ShowTime.Platform.Tests` et `ShowTime.Windows.Tests` passent par la CI : `git push origin feature/macos-app`, `gh workflow run CI --ref feature/macos-app`, `gh run watch <id> --exit-status`. Les tests propres au Mac ne tournent que sur le runner macOS.
- L'app Mac ne se compile que sur la CI (job `macos-app`) ; la capture ScreenCaptureKit (images et son) ne se teste pas sur les runners (pas d'autorisation Enregistrement de l'écran) : elle se vérifie à la main avec `docs/test-checklist.md`.
- Format du son sur le tuyau : PCM `f32le`, 48 000 Hz, 2 canaux (`new RawAudioFormat("f32le", 48000, 2)`), demandé tel quel à ScreenCaptureKit.
- Ordre des entrées ffmpeg : micro (`AudioInputs[0]`, la piste de transcription en dépend), écran, puis le tuyau (`CaptureInputs.TrailingAudioInputs`) ; l'écran et le tuyau sans sondage (`-probesize 32 -analyzeduration 0`), comme `DirectShowGdigrab`.
- Synchronisation exigée : son et image à ±0,1 s (spec), vérifiée à la main sur Mac (tâche 5).
- La piste du son du Mac garde la durée de la vidéo, même quand rien ne joue ou si la capture ne démarre pas (silence), pour que `amix` garde ses niveaux.
- BlackHole n'est plus utilisé ; s'il est installé, il reste écarté de la liste des micros (`IFfmpegPlatform.SystemAudioDevice` = `"BlackHole 2ch"`, servant à ce seul tri).
- L'arrêt reste celui de Windows : `q` à ffmpeg, puis fermeture du tuyau si ffmpeg tourne encore 3 s après (`FfmpegVideoRecorder`, inchangé).
- Textes d'interface en français ; code et commentaires en anglais ; `dotnet format` avant chaque commit ; couverture du cœur ≥ 90 %.

## Review Focus

1. **Rien ne joue pendant tout l'enregistrement** : la piste du son du Mac a la durée de la vidéo (silence), et le micro garde son niveau. → `An_idle_pipe_is_padded_with_silence` (tâche 3) et `Idle_time_is_padded_up_to_now_minus_the_slack` (tâche 1).
2. **ffmpeg échoue avant d'ouvrir le tuyau** (mauvais micro, disque plein) : la capture se libère sans rester bloquée, le FIFO est effacé. → `Disposing_before_ffmpeg_opens_returns_and_removes_the_fifo` (tâche 3).
3. **Paquet de son arrivé en retard après un comblement** : la partie déjà couverte par le silence est retirée, la piste ne prend pas d'avance. → `Audio_already_covered_by_silence_is_trimmed` (tâche 1).
4. **Autorisation Enregistrement de l'écran refusée ou capture du son qui ne démarre pas** : la vidéo s'enregistre quand même, son du Mac muet. → comportement de `MacSystemAudioCapture` (tâche 4), vérifié à la main (tâche 5).
5. **Son capté avant l'ouverture du tuyau** (ScreenCaptureKit démarre vite) : il est jeté, la piste commence à l'ouverture. → `Audio_before_the_origin_is_dropped` (tâche 1).

---

### Task 1: `AudioTimeline` et `PlanarAudio` (cœur)

**Files:**
- Create: `src/ShowTime.Core/Recording/AudioTimeline.cs`, `src/ShowTime.Core/Recording/PlanarAudio.cs`
- Test: `tests/ShowTime.Core.Tests/Recording/AudioTimelineTests.cs`, `tests/ShowTime.Core.Tests/Recording/PlanarAudioTests.cs`

**Interfaces:**
- Consumes: `RawAudioFormat` (`PipedAudio.cs`).
- Produces:
  - `public sealed class AudioTimeline(RawAudioFormat format, TimeSpan origin)` — `public static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(250);`, `long FramesWritten { get; }`, `byte[] Place(TimeSpan at, ReadOnlySpan<byte> pcm)`, `byte[] PadTo(TimeSpan now)`. Le temps est celui de la source (horloge de l'hôte sur Mac) ; une trame à `t` a l'indice `round((t - origin).TotalSeconds × SampleRate)`.
  - `public static class PlanarAudio` — `byte[] Interleave(IReadOnlyList<byte[]> planes)` : canaux en `float32` séparés (un tableau d'octets par canal, même longueur) → trames entrelacées ; un seul canal est rendu tel quel.

- [ ] **Step 1: Tests en échec** (format `f32le` 48 kHz stéréo, `BlockAlign` = 8, origine `T0 = TimeSpan.FromSeconds(100)`)

```csharp
[Fact] public void Audio_at_the_origin_is_written_as_is()
{
    var timeline = new AudioTimeline(Format, T0);
    var pcm = Frames(480);                         // 10 ms
    timeline.Place(T0, pcm).ShouldBe(pcm);
    timeline.FramesWritten.ShouldBe(480);
}

[Fact] public void A_gap_before_audio_is_filled_with_silence()
{
    var timeline = new AudioTimeline(Format, T0);
    var bytes = timeline.Place(T0 + TimeSpan.FromMilliseconds(10), Frames(480));
    bytes.Length.ShouldBe(960 * 8);
    bytes.AsSpan(0, 480 * 8).ToArray().ShouldAllBe(b => b == 0);
    timeline.FramesWritten.ShouldBe(960);
}

[Fact] public void Audio_already_covered_by_silence_is_trimmed()
{
    var timeline = new AudioTimeline(Format, T0);
    timeline.PadTo(T0 + TimeSpan.FromMilliseconds(260));          // 10 ms of silence (260 − 250 slack)
    var bytes = timeline.Place(T0 + TimeSpan.FromMilliseconds(5), Frames(480));
    bytes.Length.ShouldBe(240 * 8);                                // the last 5 ms only
    timeline.FramesWritten.ShouldBe(720);
}

[Fact] public void Audio_before_the_origin_is_dropped()
{
    var timeline = new AudioTimeline(Format, T0);
    timeline.Place(T0 - TimeSpan.FromMilliseconds(20), Frames(480)).ShouldBeEmpty();
    timeline.Place(T0 - TimeSpan.FromMilliseconds(5), Frames(480)).Length.ShouldBe(240 * 8);
}

[Fact] public void Idle_time_is_padded_up_to_now_minus_the_slack()
{
    var timeline = new AudioTimeline(Format, T0);
    timeline.PadTo(T0 + TimeSpan.FromMilliseconds(200)).ShouldBeEmpty();     // within the slack
    timeline.PadTo(T0 + TimeSpan.FromMilliseconds(1250)).Length.ShouldBe(48000 * 8);
    timeline.PadTo(T0 + TimeSpan.FromMilliseconds(1250)).ShouldBeEmpty();     // already level
}

// PlanarAudioTests
[Fact] public void Two_planes_are_interleaved()
{
    var left = Floats(1f, 2f); var right = Floats(-1f, -2f);
    PlanarAudio.Interleave([left, right]).ShouldBe(Floats(1f, -1f, 2f, -2f));
}

[Fact] public void One_plane_is_kept_as_it_is() => PlanarAudio.Interleave([Floats(1f, 2f)]).ShouldBe(Floats(1f, 2f));
```

`Frames(n)` : `n × 8` octets non nuls (pour les distinguer du silence) ; `Floats(...)` : `MemoryMarshal.AsBytes` d'un `float[]` copié.

- [ ] **Step 2: Vérifier l'échec** — `dotnet test --project tests/ShowTime.Core.Tests`. Expected: échec de compilation (`AudioTimeline`, `PlanarAudio`).
- [ ] **Step 3: Implémenter.** `Place` : indice de début (arrondi) ; s'il dépasse `FramesWritten`, silence jusque-là puis le paquet ; s'il est en deçà, ne garder que la fin non couverte (rien si tout est couvert). `PadTo` : silence jusqu'à l'indice de `now − Slack` s'il dépasse `FramesWritten`, sinon vide. Doc XML : pourquoi (ffmpeg lit le PCM sans horodatage, le nombre de trames fait le temps ; ScreenCaptureKit peut ne rien livrer quand rien ne joue, et ses paquets arrivent avec quelques dizaines de ms de retard, d'où la marge).
- [ ] **Step 4: Vérifier le succès** — même commande ; PASS.
- [ ] **Step 5: Commit** — `git commit -m "Place captured sound on the timeline and pad the gaps with silence"`

---

### Task 2: `AvFoundation` lit le son du Mac dans le tuyau

**Files:**
- Modify: `src/ShowTime.Core/Recording/AvFoundation.cs`
- Test: `tests/ShowTime.Core.Tests/Recording/AvFoundationTests.cs`

**Interfaces:**
- Produces: `AvFoundation.BuildInputs(area, options, systemAudio)` lit le son du Mac dans `systemAudio` (entrée en fin de liste) ; `CanRecordSystemAudio(devices)` vaut toujours `true` ; `SystemAudioDevice` reste `"BlackHole 2ch"` (tri des micros seulement) ; `SystemAudioRequirement` = « l'autorisation Enregistrement de l'écran ».

- [ ] **Step 1: Tests en échec** (avec `Pipe = new PipedAudio("/tmp/lt.pcm", new RawAudioFormat("f32le", 48000, 2))`)
  - `The_computer_sound_is_read_from_the_pipe_after_the_screen` : `BuildInputs(area, new VideoOptions(30, "MacBook Pro Microphone", true), Pipe)` → `AudioInputs` vaut `[["-f", "avfoundation", "-thread_queue_size", "1024", "-i", ":MacBook Pro Microphone"]]` ; `TrailingAudioInputs` vaut `[["-f", "f32le", "-ar", "48000", "-ac", "2", "-thread_queue_size", "1024", "-probesize", "32", "-analyzeduration", "0", "-i", "/tmp/lt.pcm"]]` ; `VideoInput` contient la séquence `"-probesize", "32", "-analyzeduration", "0"` avant `"-i"` ; aucune entrée ne contient `BlackHole`.
  - `Without_the_computer_sound_the_screen_is_probed_as_before` : sans son du Mac, `VideoInput` est inchangé (pas de `-probesize`) et `TrailingAudioInputs` est null.
  - `The_computer_sound_needs_its_pipe` : `SystemAudio = true` avec `systemAudio: null` → `ArgumentException`.
  - Remplacer `The_computer_sound_needs_BlackHole` et `BlackHole_is_tried_anyway_when_ffmpeg_could_not_list_the_devices` par `The_computer_sound_needs_no_driver` : `CanRecordSystemAudio([])` et `CanRecordSystemAudio(["MacBook Pro Microphone"])` valent `true` (commentaire `// Deliberate change: ShowTime captures the Mac's sound itself; BlackHole is no longer needed.`).
  - `Each_audio_input_is_opened_by_name` : n'attend plus l'entrée `:BlackHole 2ch`.
- [ ] **Step 2: Vérifier l'échec** — `dotnet test --project tests/ShowTime.Core.Tests`.
- [ ] **Step 3: Implémenter**, sur le modèle de `DirectShowGdigrab.BuildInputs` (même `NoProbe`, même entrée de tuyau ; pas de `DownmixToStereo`, le son est déjà stéréo). Doc XML de la classe : le son du Mac vient de ShowTime par un tuyau ; BlackHole n'est plus lu.
- [ ] **Step 4: Vérifier le succès** — même commande ; PASS.
- [ ] **Step 5: Commit** — `git commit -m "Read the Mac's sound from ShowTime's pipe instead of BlackHole"`

---

### Task 3: `MacAudioPipe` (FIFO pour ffmpeg)

**Files:**
- Create: `src/ShowTime.Platform.MacOS/MacAudioPipe.cs`
- Modify: `src/ShowTime.Platform.MacOS/Native.cs` (`mkfifo`, `open`, `close` de libc)
- Test: `tests/ShowTime.Platform.Tests/MacOS/MacAudioPipeTests.cs` (sautés hors macOS, comme les tests voisins)

**Interfaces:**
- Consumes: `AudioTimeline` (tâche 1), `PipedAudio`, `RawAudioFormat`.
- Produces: `public sealed class MacAudioPipe : IDisposable`
  - `public MacAudioPipe(string directory, RawAudioFormat format, Func<TimeSpan> clock)` — crée `showtime-audio-<pid>-<guid>.pcm` dans `directory` (`mkfifo`, mode `0600`) et attend ffmpeg sur un thread de fond (ouverture en écriture, bloquante) ;
  - `PipedAudio Input { get; }` ;
  - `event Action? Opened` — levé sur ce thread quand ffmpeg ouvre le FIFO ; l'origine du temps est `clock()` à cet instant ;
  - `void Write(TimeSpan at, ReadOnlySpan<byte> pcm)` — sûr entre threads ; ignoré avant `Opened` et après `Dispose` ; passe par `AudioTimeline.Place` ;
  - un minuteur de 20 ms appelle `AudioTimeline.PadTo(clock())` tant que le FIFO est ouvert ;
  - `Dispose()` — arrête le minuteur et l'écriture, ferme le FIFO (ffmpeg reçoit la fin du flux), débloque l'attente si ffmpeg ne l'a jamais ouvert (ouverture en lecture `O_RDONLY | O_NONBLOCK` puis fermeture), efface le fichier.
  - Écriture : une file (`Channel<byte[]>`) vidée par un seul thread dans le `FileStream` ; une `IOException` (ffmpeg parti) arrête l'écriture sans exception vers l'appelant.

- [ ] **Step 1: Tests en échec** (`clock` = `Stopwatch` démarré par le test ; lecteur = `FileStream` ouvert en lecture sur `Input.PipePath` dans une tâche)
  - `ffmpeg_opening_the_fifo_starts_the_timeline` : `Opened` est levé quand le lecteur ouvre ; un `Write(clock(), Frames(480))` aussitôt après se retrouve dans les octets lus (les 480 trames non nulles, éventuellement précédées de quelques trames de silence).
  - `An_idle_pipe_is_padded_with_silence` : sans `Write`, en lisant 1,5 s, on reçoit `(1,5 s − Slack) × 48 000 × 8` octets à ±60 ms près, tous nuls.
  - `Disposing_before_ffmpeg_opens_returns_and_removes_the_fifo` : `Dispose()` sans lecteur rend la main en moins d'une seconde, `File.Exists(Input.PipePath)` est faux.
  - `ffmpeg_reads_the_pipe` (sauté sans ffmpeg sur le PATH ; la CI y met le ffmpeg livré) : `ffmpeg -f f32le -ar 48000 -ac 2 -i <fifo> -t 2 out.wav`, sans aucun `Write` ; `out.wav` dure 2 s (`ffmpeg -i` ou la taille de l'en-tête WAV) : le comblement tient la piste à la bonne durée.
- [ ] **Step 2: Vérifier l'échec** — CI (Platform.Tests sur macOS). Expected: échec de compilation.
- [ ] **Step 3: Implémenter.** `LibraryImport("libc", SetLastError = true, StringMarshalling = Utf8)` : `int mkfifo(string path, uint mode)`, `int open(string path, int flags)`, `int close(int fd)` ; constantes `O_RDONLY = 0`, `O_NONBLOCK = 0x0004` (valeurs macOS). Un `mkfifo` qui échoue lève une `IOException` avec `Marshal.GetLastPInvokeError()`.
- [ ] **Step 4: Vérifier le succès** — CI : job `portable (macos-latest)` vert, les quatre tests exécutés (pas sautés) ; les autres jobs verts.
- [ ] **Step 5: Commit** — `git commit -m "Feed ffmpeg the Mac's sound through a FIFO, padded with silence"`

---

### Task 4: capture ScreenCaptureKit dans l'app Mac, branchement, barre et réglages

**Files:**
- Create: `src/ShowTime.Mac/Platform/MacSystemAudio.cs`
- Modify: `src/ShowTime.Mac/Composition.cs`, `src/ShowTime.Mac/Recording/RecordingBar.cs:148-153,247`, `src/ShowTime.Mac/Settings/SettingsWindowController.cs:146,232-259`

**Interfaces:**
- Consumes: `MacAudioPipe` (tâche 3), `PlanarAudio` (tâche 1), `ISystemAudioSource` / `ISystemAudioCapture` (cœur), `AvFoundation.CanRecordSystemAudio` (tâche 2).
- Produces: `internal sealed class MacSystemAudio : ISystemAudioSource` (`IsAvailable()` → `true` ; `Start()` → `MacSystemAudioCapture`) et `internal sealed class MacSystemAudioCapture : ISystemAudioCapture`.

- [ ] **Step 1: `MacSystemAudioCapture`**
  - Crée `new MacAudioPipe(Path.GetTempPath(), new RawAudioFormat("f32le", 48000, 2), HostClock)` avec `HostClock = () => TimeSpan.FromSeconds(CMClock.HostTimeClock.CurrentTime.Seconds)` (même horloge que les dates des paquets de ScreenCaptureKit) ; `Input` = celui du tuyau.
  - Sur `Opened` : obtient l'écran principal (`SCShareableContent`), filtre sans exclusion, `SCStreamConfiguration` avec `CapturesAudio = true`, `SampleRate = 48000`, `ChannelCount = 2`, `ExcludesCurrentProcessAudio = true`, image minimale (2 × 2, une image par seconde, sans curseur) ; ajoute une sortie (objet `NSObject` implémentant `ISCStreamOutput`) pour l'audio et pour l'écran (l'image est ignorée : sans sortie écran, ScreenCaptureKit remplit le journal d'avertissements) ; `StartCapture`.
  - Chaque paquet audio : date = `PresentationTimeStamp` en secondes ; plans du `CMSampleBuffer` (liste de tampons audio) → `PlanarAudio.Interleave` (ou tel quel s'il est déjà entrelacé, d'après la description du format : float 32 bits, 48 kHz, 2 canaux attendus ; sinon le paquet est ignoré et consigné une fois) → `pipe.Write(date, octets)`.
  - Échec du démarrage (autorisation refusée, erreur) : consigné au journal ; le tuyau continue de combler par du silence (la vidéo s'enregistre, son du Mac muet), comme `WasapiSystemAudio` quand la sortie est perdue.
  - `Dispose` : `StopCapture` (sans attendre au-delà de 1 s), puis `pipe.Dispose()`.
- [ ] **Step 2: Branchement** — `Composition` : `services.AddSingleton<ISystemAudioSource, MacSystemAudio>();` (`FfmpegVideoRecorderFactory` le reçoit par injection).
- [ ] **Step 3: Barre et réglages** — comme sous Windows : `_hasSystemAudio = _platform.CanRecordSystemAudio(devices)` (et à la construction `CanRecordSystemAudio([])`) au lieu de `devices.Contains(_platform.SystemAudioDevice)` ; même chose dans les réglages ; aide des réglages : « Son de l'ordinateur : capté par ShowTime, sans pilote à installer. » ; le résumé XML qui parle de BlackHole est corrigé.
- [ ] **Step 4: Vérifier** — CI dispatchée. Expected: `macos-app` vert (compile, trim sans avertissement, format), autres jobs verts.
- [ ] **Step 5: Commit** — `git commit -m "Capture the Mac's sound with ScreenCaptureKit"`

---

### Task 5: vérification de la synchronisation sur Mac, documentation

**Files:**
- Create: `build/sync-check.sh`
- Modify: `.github/workflows/ci.yml` (job `portable`, macOS : auto-test du script), `docs/test-checklist.md` (macOS, étape vidéo), `README.md`, `CLAUDE.md` (« Computer sound »)

**Interfaces:**
- Produces: `build/sync-check.sh clip <out.mp4>` (clip de test : écran noir avec un flash blanc de 0,1 s toutes les 2 s, et un bip de 1 kHz de 0,1 s en même temps, 12 s) et `build/sync-check.sh measure <recording.mp4>` (pour chaque flash, l'écart avec le bip le plus proche, puis la moyenne ; code de sortie 0 si chaque écart est dans ±0,1 s). Utilise `ffmpeg` du PATH ou `FFMPEG` (sur Mac : `/Applications/ShowTime.app/Contents/MacOS/ffmpeg`). Mesure comme `SyncTests` : `signalstats` (YAVG > 128) pour les flashs, `silencedetect=noise=-30dB:d=0.05` pour les bips.

- [ ] **Step 1: Écrire le script** (`set -euo pipefail`, sorties de ffmpeg dans des variables, comme `fetch-ffmpeg.sh`).
- [ ] **Step 2: Auto-test en CI** — job `portable`, macOS seulement, après « Install ffmpeg » : `bash build/sync-check.sh clip "$RUNNER_TEMP/sync.mp4" && bash build/sync-check.sh measure "$RUNNER_TEMP/sync.mp4"` (le clip mesuré contre lui-même : écarts ≈ 0).
- [ ] **Step 3: Checklist macOS** (étape vidéo, remplace le prérequis BlackHole) :
  - Son du Mac sans pilote : sur un Mac sans BlackHole, « Son du Mac » est actif ; une vidéo qui joue pendant l'enregistrement s'entend dans le MP4.
  - Synchronisation : `bash build/sync-check.sh clip ~/Desktop/sync.mp4`, le lire en plein écran dans QuickTime, l'enregistrer avec ShowTime (son du Mac seul, plein écran), puis `bash build/sync-check.sh measure <la vidéo>` : tous les écarts dans ±0,1 s ; sinon, noter la moyenne et la signaler (elle dit dans quel sens ajuster l'ordre ou le sondage des entrées).
  - Silence : enregistrer 10 s sans rien jouer, avec le micro : la piste a la durée de la vidéo et la voix garde son niveau.
  - Capture du son en échec (le journal `~/Library/Logs/ShowTime` le dit) : la vidéo s'enregistre quand même, avec un son du Mac muet de la durée de la vidéo.
- [ ] **Step 4: README et CLAUDE.md** — README : plus de BlackHole ni de « Périphérique à sorties multiples » ; « Son du Mac : capté par ShowTime, sans pilote ». CLAUDE.md, « Computer sound » : macOS aussi capte lui-même (`MacSystemAudio` : ScreenCaptureKit → `MacAudioPipe`, FIFO), `AudioTimeline` place les paquets à leur date et comble par du silence, BlackHole seulement écarté des micros ; vérification de la synchronisation sur Mac par `build/sync-check.sh`.
- [ ] **Step 5: Vérifier** — CI dispatchée ; Expected: `portable (macos-latest)` vert avec l'auto-test du script.
- [ ] **Step 6: Commit** — `git commit -m "Check the Mac's sound sync by hand with build/sync-check.sh, and document it"`
