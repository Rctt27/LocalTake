# Sous-titres sur Mac et pas de 5 s du lecteur — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** l'app Mac transcrit le micro en sous-titres modifiables exactement comme l'app Windows (whisper.cpp et le modèle livrés dans l'app), et le lecteur des deux apps avance ou recule de 5 s avec ←/→.

**Architecture:** whisper.cpp entre dans le bundle par les bibliothèques `macos-arm64` / `macos-x64` du paquet `Whisper.net.Runtime`, et le modèle dans `Contents/Resources/models` ; la CI le prouve en lançant `--diagnostics` sur l'app empaquetée. Ce que le panneau de sous-titres Windows décide aujourd'hui dans son code-behind (état de la transcription, textes d'état, enregistrement différé, validation des temps, ligne active) passe dans le cœur (`SubtitleEditor`), avec trois petites règles partagées (placement du bandeau, lisibilité d'un bloc, bascule « Sous-titres » de la barre) et le pas du lecteur (`PlayerSeek`) ; l'app Windows s'en sert sans changer de comportement, et l'app Mac n'écrit que des vues AppKit par-dessus.

**Tech Stack:** .NET 10, C#, Whisper.net 1.9.1, AppKit / AVKit (liaisons .NET pour macOS), WPF, xunit v3 + Shouldly + FakeTimeProvider + MockFileSystem.

**Spec:** `docs/superpowers/specs/2026-10-24-macos-parity-design.md` (objectifs 1 et 4). Plan 4 sur 4.

## Global Constraints

- Branche `feature/macos-app` ; commits terminés par les lignes d'attribution de la session.
- Tests en local : `tests/ShowTime.Core.Tests` et `tests/ShowTime.Infrastructure.IntegrationTests` seulement. `ShowTime.Platform.Tests` et `ShowTime.Windows.Tests` passent par la CI (`gh workflow run CI --ref feature/macos-app`, puis `gh run watch <id> --exit-status`) : ils affichent des fenêtres ou captent du son, ce qui fait planter une autre application sur le PC de l'utilisateur.
- L'app Mac ne se compile que sur la CI (job `macos-app`) et n'a pas de tests automatisés : ce qui se teste va dans le cœur ; le reste se vérifie à la main (`docs/test-checklist.md`, artefact `ShowTime-macos`).
- Couverture du cœur ≥ 90 % (`python build/check-coverage.py`).
- L'app ne télécharge rien : modèle `ggml-small-q5_1.bin`, révision `5359861c739e955e79d9a303bcbc70fb988958b1`, SHA-256 `ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb`, récupéré au build seulement.
- Pas de régression Windows : mêmes textes, mêmes comportements, `TranscriptPanelTests` / `VideoPreview*Tests` / `RecordingBarTests` inchangés et verts, sauf le pas de 10 s → 5 s (infobulles « Reculer de 5 secondes (←) », « Avancer de 5 secondes (→) »).
- Textes d'interface en français, repris mot pour mot de Windows quand l'élément existe des deux côtés ; code et commentaires en anglais ; `dotnet format` avant chaque commit.
- macOS 14 minimum, app universelle, trimmée : un avertissement de trim fait échouer le build.

## Review Focus

1. **whisper.cpp ne se charge pas depuis le bundle** (bibliothèque absente, mauvaise architecture, signature) : « Générer les sous-titres » et la bascule sont grisés avec l'infobulle du modèle, et `--diagnostics` dit pourquoi. → la CI lance `--diagnostics` sur l'app empaquetée en arm64, et en x86_64 sous Rosetta quand le runner l'a (tâche 1).
2. **Une modification puis la fenêtre fermée avant 0,5 s** : le `.srt` contient la modification. → `Closing_saves_a_pending_change` (tâche 4).
3. **« Supprimer » ou « Refaire » pendant une transcription** : elle s'arrête, sa piste est effacée, rien n'est réécrit dans un `.srt` envoyé à la Corbeille. → `Discarding_cancels_the_transcription_and_saves_nothing` (tâche 4).
4. **←/→ près du début ou de la fin** : la position reste dans la vidéo. → `Back_stops_at_the_start`, `Forward_stops_at_the_end` (tâche 2).
5. **←/→, Espace ou ⌘Z pendant la saisie d'un sous-titre** : ils agissent dans le champ, pas sur le lecteur ni sur l'historique des blocs. → étape manuelle de la checklist (tâche 7).

---

### Task 1: whisper.cpp et le modèle dans l'app Mac, prouvés par la CI

**Files:**
- Create: `build/fetch-whisper-model.sh`
- Modify: `src/ShowTime.Infrastructure/ShowTime.Infrastructure.csproj` (cible `DropWhisperRuntimes`), `src/ShowTime.Mac/ShowTime.Mac.csproj`, `src/ShowTime.Mac/Composition.cs`, `build/package-macos.sh`, `.github/workflows/ci.yml` (job `macos-app`), `.github/workflows/release.yml` (étape « Add the Whisper model » de Windows)

**Interfaces:**
- Produces: `bash build/fetch-whisper-model.sh <dir>` écrit `<dir>/ggml-small-q5_1.bin`, vérifié par SHA-256, et échoue sinon ; le bundle contient `Contents/Resources/models/ggml-small-q5_1.bin` et les bibliothèques de Whisper là où Whisper.net les cherche (`AppContext.BaseDirectory` + `runtimes/macos-<arch>/`, soit `Contents/MonoBundle/runtimes/…`).

- [ ] **Step 1: écrire `build/fetch-whisper-model.sh`** (style de `fetch-ffmpeg.sh` : `set -euo pipefail`, révision, nom et SHA-256 de Global Constraints en variables, `curl -sSfL`, `sha256sum` ou `shasum -a 256`). `release.yml` (Windows) l'appelle à la place de ses lignes `curl` / `sha256sum` et perd ses variables `WHISPER_MODEL*`.

- [ ] **Step 2: garder les bibliothèques macOS de Whisper dans l'app Mac.** `WhisperRuntimes` prend une troisième valeur, `macos` : `DropWhisperRuntimes` retire alors tous les éléments `runtimes/…` sauf ceux dont le `TargetPath` commence par `runtimes/macos-` (avec `false`, il retire tout, comme aujourd'hui). L'app Mac passe `WhisperRuntimes=macos` ; les `ReferenceNativeSymbol … Ignore` restent (Whisper.net charge `libwhisper.dylib` à l'exécution), leur commentaire le dit. Composition : `DiagnosticsReport` sans l'argument `false`.

- [ ] **Step 3: `package-macos.sh`** : appelle `fetch-whisper-model.sh "$app/Contents/Resources/models"` (l'artefact de CI porte donc le modèle : le Mac se teste sans rien installer) ; vérifie que `Contents/MonoBundle/runtimes/macos-arm64/libwhisper.dylib` et `…/macos-x64/libwhisper.dylib` existent (`fail` sinon, avec `find "$app" -name 'libwhisper*'` dans le message) ; signe chaque `.dylib` de `Contents/MonoBundle/runtimes` avec `$identity` avant l'app, comme `ffmpeg`.

- [ ] **Step 4: preuve sur la CI.** Dans le job `macos-app`, après l'empaquetage : extraire le zip dans `$RUNNER_TEMP`, lancer `ShowTime.app/Contents/MacOS/ShowTime --diagnostics` sous une limite de 300 s (`perl -e 'alarm shift; exec @ARGV' 300 …` : macOS n'a pas `timeout`), sans exiger un code de sortie nul (l'écran n'est pas autorisé sur le runner), puis exiger la ligne `[ok]   transcription:` dans la sortie (`grep -F`). Même chose sous `arch -x86_64` si `arch -x86_64 /usr/bin/true` réussit, sinon un `::warning::` « Rosetta absent : Whisper x86_64 non vérifié ».

- [ ] **Step 5: CI.** Commit, push, `gh workflow run CI --ref feature/macos-app`, `gh run watch`. Attendu : `macos-app` vert, la ligne `[ok]   transcription: …/Contents/Resources/models/ggml-small-q5_1.bin (… MB), language fr` dans son journal. Si les bibliothèques ne sont pas sous `Contents/MonoBundle/runtimes` : les y placer depuis le csproj Mac (éléments `None` sur `$(NuGetPackageRoot)whisper.net.runtime/1.9.1/build/macos-*/*.dylib`, `Link="runtimes/macos-<arch>/%(Filename)%(Extension)"`, `CopyToOutputDirectory="PreserveNewest"`), puis relancer.

- [ ] **Step 6: commit** `Ship whisper.cpp and the model in the Mac app`.

### Task 2: pas de 5 s du lecteur (`PlayerSeek`), appliqué à Windows

**Files:**
- Create: `src/ShowTime.Core/Recording/PlayerSeek.cs`, `tests/ShowTime.Core.Tests/Recording/PlayerSeekTests.cs`
- Modify: `src/ShowTime.Windows/Recording/VideoPreviewWindow.xaml` (deux infobulles), `src/ShowTime.Windows/Recording/VideoPreviewWindow.xaml.cs` (`JumpSeconds` supprimé, résumé de classe « 5-second jumps »)

**Interfaces:**
- Produces: `public static class PlayerSeek { public static readonly TimeSpan Step; public static TimeSpan Back(TimeSpan position); public static TimeSpan Forward(TimeSpan position, TimeSpan duration); }`

- [ ] **Step 1: tests qui échouent**

```csharp
[Fact] public void The_step_is_five_seconds() => PlayerSeek.Step.ShouldBe(TimeSpan.FromSeconds(5));
[Fact] public void Back_goes_five_seconds_back() => PlayerSeek.Back(S(12)).ShouldBe(S(7));
[Fact] public void Back_stops_at_the_start() => PlayerSeek.Back(S(3)).ShouldBe(TimeSpan.Zero);
[Fact] public void Forward_goes_five_seconds_on() => PlayerSeek.Forward(S(12), S(60)).ShouldBe(S(17));
[Fact] public void Forward_stops_at_the_end() => PlayerSeek.Forward(S(58), S(60)).ShouldBe(S(60));
// An unknown duration (zero, before the player knows it) does not hold the position back.
[Fact] public void Forward_without_a_duration_still_moves() => PlayerSeek.Forward(S(12), TimeSpan.Zero).ShouldBe(S(17));
```

- [ ] **Step 2:** `dotnet test --project tests/ShowTime.Core.Tests` → échec de compilation (`PlayerSeek` inconnu).
- [ ] **Step 3: implémenter `PlayerSeek`** ; Windows : `SeekBar.LargeChange = PlayerSeek.Step.TotalSeconds`, ←/→ et les boutons passent par `Back` / `Forward`.
- [ ] **Step 4:** tests du cœur verts ; `dotnet build ShowTime.slnx -c Release` sans avertissement.
- [ ] **Step 5: commit** `Jump 5 seconds in the player`.

### Task 3: règles d'affichage partagées (bandeau, lisibilité, bascule de la barre)

**Files:**
- Create: `src/ShowTime.Core/Transcription/SubtitlePlacement.cs`, `src/ShowTime.Core/Transcription/CueReadability.cs`, `src/ShowTime.Core/Recording/SubtitleToggle.cs`, et leurs tests dans `tests/ShowTime.Core.Tests/Transcription/` et `…/Recording/`
- Modify: `src/ShowTime.Windows/Recording/VideoPreviewWindow.xaml.cs` (`PlaceSubtitle`), `src/ShowTime.Windows/Recording/TranscriptPanel.xaml.cs` (`CueRow.ShowReadability`), `src/ShowTime.Windows/Recording/RecordingBar.xaml.cs` (état de `TranscriptionToggle`)

**Interfaces:**
- Produces:
  - `public sealed record SubtitlePlacement(double FontSize, double MaxWidth, double BottomMargin) { public static SubtitlePlacement For(double hostWidth, double hostHeight, double videoWidth, double videoHeight); }`, en unités de la vue (points ou DIP) ; une vidéo de taille inconnue (0) occupe tout l'hôte.
  - `public enum ReadabilityLevel { Easy, Notice, Problem }` ; `public sealed record CueReadability(string Speed, ReadabilityLevel Level, string ToolTip) { public static CueReadability Of(SubtitleCue cue); }` (profil `SubtitleProfile.Tutorial`).
  - `public sealed record ToggleState(bool IsChecked, bool IsEnabled, string ToolTip)` ; `public static class SubtitleToggle { public static ToggleState For(bool canTranscribe, bool microphoneOn, bool transcribe, bool locked); }`.

- [ ] **Step 1: tests qui échouent**, valeurs reprises du code Windows actuel :

```csharp
// SubtitlePlacementTests: a 16:9 video in a 1000x1000 host is 1000x562.5, letterboxed by 218.75 above and below.
var p = SubtitlePlacement.For(1000, 1000, 1920, 1080);
p.FontSize.ShouldBe(562.5 * 0.045, 1e-9);
p.MaxWidth.ShouldBe(900, 1e-9);
p.BottomMargin.ShouldBe(218.75 + 562.5 * 0.05, 1e-9);
SubtitlePlacement.For(400, 100, 0, 0).FontSize.ShouldBe(12);           // never below 12
SubtitlePlacement.For(800, 600, 0, 0).MaxWidth.ShouldBe(720, 1e-9);    // unknown size: the whole host

// CueReadabilityTests
CueReadability.Of(Cue(0, 2, "Bonjour")).ShouldBe(new CueReadability("4 car./s", ReadabilityLevel.Easy,
    "Caractères affichés par seconde : facile à lire (15 au plus, idéalement)."));
CueReadability.Of(<a cue at 16 car./s>).Level.ShouldBe(ReadabilityLevel.Notice);   // tooltip = the warnings, one per line
CueReadability.Of(<a cue at 20 car./s>).Level.ShouldBe(ReadabilityLevel.Problem);

// SubtitleToggleTests: the four tooltips of RecordingBar.xaml.cs, word for word
SubtitleToggle.For(false, true, true, false).ShouldBe(new ToggleState(false, false, "Modèle de transcription introuvable : voir les réglages."));
SubtitleToggle.For(true, false, true, false).ShouldBe(new ToggleState(false, false, "Les sous-titres transcrivent le micro : activez-le d'abord."));
SubtitleToggle.For(true, true, true, false).IsChecked.ShouldBeTrue();
SubtitleToggle.For(true, true, true, true).IsEnabled.ShouldBeFalse();   // locked once recording
SubtitleToggle.For(true, true, false, false).ToolTip.ShouldBe("Pas de sous-titres. Cliquer pour transcrire le micro après l'enregistrement.");
```

- [ ] **Step 2:** tests du cœur → échec de compilation.
- [ ] **Step 3: implémenter** les trois types (la vitesse arrondie comme aujourd'hui : `Math.Round(SubtitleProfile.ReadingSpeed(cue))` en culture courante) ; Windows les utilise à la place de son code, les pinceaux restent dans la vue (`Problem` → `ErrorBrush`, `Notice` → `NoticeBrush`, `Easy` → `DimBrush`).
- [ ] **Step 4:** tests du cœur verts et couverture ≥ 90 % ; build Windows sans avertissement ; CI (job `windows`) verte : `RecordingBarTests`, `TranscriptPanelTests` (`A_cue_too_fast_to_read_says_so_beside_its_times`) et `VideoPreviewSubtitleTests` inchangés.
- [ ] **Step 5: commit** `Share the subtitle band, readability and toggle rules`.

### Task 4: `SubtitleEditor`, la logique du panneau de sous-titres dans le cœur

**Files:**
- Create: `src/ShowTime.Core/Transcription/SubtitleEditor.cs`, `tests/ShowTime.Core.Tests/Transcription/SubtitleEditorTests.cs`
- Modify: `src/ShowTime.Windows/Recording/TranscriptPanel.xaml.cs` (délègue au `SubtitleEditor` ; ne garde que les lignes, le focus, le centrage et les pinceaux)

**Interfaces:**
- Consumes: `SubtitleFile`, `Transcriptions`, `TranscriptionJob`, `SubtitleDocument`, `Timecode`, `IUiThread`, `TimeProvider`.
- Produces:

```csharp
public sealed record SubtitleStatus(string Text, bool IsError, bool ShowsProgress, double Progress, bool CanRetry);

public sealed class SubtitleEditor(SubtitleFile file, Transcriptions transcriptions, TimeProvider clock, IUiThread uiThread) : IDisposable
{
    public static readonly TimeSpan SaveDelay;                          // 500 ms
    public void Load(string videoPath, TranscriptionJob? job);          // job, else the .srt beside the video (empty or unreadable = none)
    public void Generate(string? microphoneTrack);                      // "Générer les sous-titres"
    public void Retry();                                                // "Réessayer"
    public void Close(bool discard);                                    // discard: cancel the job, drop the pending save; else save now
    public void SetDuration(TimeSpan duration);
    public void ShowPosition(TimeSpan position);
    public bool HasSubtitles { get; }
    public bool CanGenerate { get; }
    public IReadOnlyList<SubtitleCue> Cues { get; }
    public IReadOnlyList<TimelineEntry> Timeline { get; }
    public int ActiveIndex { get; }                                     // -1 before anything is shown
    public string? CueText { get; }                                     // the cue at the position, null in a gap or an empty cue
    public SubtitleStatus Status { get; }
    public string? CommitStart(int cueIndex, string text);              // null: applied or unchanged; else the message to show
    public string? CommitEnd(int cueIndex, string text);
    public bool CommitText(int cueIndex, string text);                  // false: refused, show the cue again
    public void Remove(int cueIndex);
    public int AddInGap(GapEntry gap);                                  // the new cue's index, or -1
    public bool Undo();
    public bool Redo();
    public event Action<bool>? TimelineChanged;                         // rows to rebuild; true: centre the active row
    public event Action? ActiveChanged;
    public event Action? StatusChanged;                                 // Status, CanGenerate or HasSubtitles may have changed
    public event Action<string?>? CueShown;                             // only when the text to show changes
}
```

Les textes d'état sont ceux de `TranscriptPanel` aujourd'hui, mot pour mot (attente, « Transcription du micro… 42 % », erreur du job, « Transcription annulée. », « Aucune parole détectée… », « 1 sous-titre », « N sous-titres », et « Les sous-titres n'ont pas pu être enregistrés : … » sur une deuxième ligne avec `IsError`). Un temps illisible donne « Temps illisible : écrivez-le par exemple 01:02.345. ». L'enregistrement différé est un minuteur de `clock` relancé à chaque modification, dont le rappel passe par `uiThread.Post`.

- [ ] **Step 1: tests qui échouent** (MockFileSystem, `FakeTimeProvider`, `ImmediateUiThread`, `FakeTranscriber`, `FakeAudioExtractor`, `FakeRecordingStatus` de `Fakes.cs` ; vidéo de 10 s avec « Un » de 1 à 3 s et « Deux » de 5 à 8 s, comme `TranscriptPanelTests`) :
  - `Saved_subtitles_load_with_the_gaps_between_them` : `Timeline` = gap 0–1, cue, gap 3–5, cue, gap 8–10 ; `Status.Text == "2 sous-titres"` ; `CanGenerate` faux.
  - `An_empty_srt_offers_to_generate` : `HasSubtitles` faux, `CanGenerate` vrai.
  - `The_position_picks_its_cue_or_gap` : `ShowPosition(S(2))` → `ActiveIndex == 1`, `CueText == "Un"` ; `ShowPosition(S(4))` → 2, `CueText == null` ; `CueShown` levé une fois par changement.
  - `An_edit_is_saved_half_a_second_later` : `CommitText(1, "Deux,\r\ntrois")` ; rien dans le `.srt` à 499 ms ; à 500 ms, « Deux,\ntrois ».
  - `Closing_saves_a_pending_change` : `CommitText` puis `Close(discard: false)` → `.srt` à jour sans avancer l'horloge.
  - `Discarding_cancels_the_transcription_and_saves_nothing` : job en cours, `Close(discard: true)` → job `Canceled`, aucune écriture après.
  - `A_refused_time_returns_the_document_message` et `An_unreadable_time_is_explained` : `CommitEnd(0, "00:06.000")` (chevauche « Deux ») → le message de `SubtitleDocument` ; `CommitStart(0, "abc")` → « Temps illisible : écrivez-le par exemple 01:02.345. » ; le document est inchangé.
  - `The_transcription_progress_and_result_show` : `Status.ShowsProgress` et « Transcription du micro… » pendant le job ; à la fin, `Cues` = le résultat et `TimelineChanged(true)`.
  - `A_failed_transcription_offers_retry` : `Status.CanRetry`, `Status.Text` = l'erreur ; `Retry()` relance le job.
  - `Undo_and_redo_go_through_the_document`.
  - `A_save_that_fails_says_so_under_the_count` : `.srt` en lecture seule → `Status.IsError`, deuxième ligne « Les sous-titres n'ont pas pu être enregistrés : … ».
- [ ] **Step 2:** tests du cœur → échec de compilation.
- [ ] **Step 3: implémenter `SubtitleEditor`** en déplaçant la logique de `TranscriptPanel` (`Load`, `Attach`, `OnJobChanged`, `SetDocument`, `UpdateStatus`, `Save`, `CommitTime`, `CommitText`, `UpdateActive`, `ShowCue`).
- [ ] **Step 4: `TranscriptPanel` délègue** : `Load(videoPath, file, transcriptions, job)` garde sa signature (les tests Windows l'appellent) et crée l'éditeur avec `TimeProvider.System` et un `IUiThread` sur son `Dispatcher` ; `CanGenerate`, `HasSubtitles`, `Cues`, `Undo`, `Redo`, `Generate`, `Close`, `SetDuration`, `ShowPosition` et les événements `CueShown` / `SubtitlesChanged` relaient l'éditeur ; `Sync` lit `editor.Timeline`, `UpdateActive` lit `editor.ActiveIndex`.
- [ ] **Step 5:** tests du cœur verts, couverture ≥ 90 % ; build Windows sans avertissement ; CI verte, avec les 9 tests de `TranscriptPanelTests` et ceux de `VideoPreviewGenerateTests` / `VideoPreviewSubtitleTests` inchangés.
- [ ] **Step 6: commit** `Move the subtitle panel's rules to the core`.

### Task 5: panneau de sous-titres Mac (`TranscriptPanelView`)

**Files:**
- Create: `src/ShowTime.Mac/Video/TranscriptPanelView.cs`

**Interfaces:**
- Consumes: `SubtitleEditor` (tâche 4), `CueReadability` (tâche 3), `Timecode`, `SubtitleDocument.MinDuration`.
- Produces: `internal sealed class TranscriptPanelView : NSView` avec `TranscriptPanelView(SubtitleEditor editor)`, `bool IsEditing { get; }`, `event Action<TimeSpan>? SeekRequested`, `event Action? EditingStarted`, `event Action? EditingEnded` (le lecteur reprend le clavier).

- [ ] **Step 1: écrire la vue**, sur le modèle de `TranscriptPanel` (Windows) pour le contenu et des conventions Mac pour la forme : en haut le texte d'état (`Status`, rouge si `IsError`), un `NSProgressIndicator` (`ShowsProgress`) et « Réessayer » (`CanRetry`) ; dessous un `NSScrollView` d'un `NSStackView` vertical de lignes reconstruites sur `TimelineChanged` (les lignes de bloc sont mises à jour sur place, seules les lignes d'intervalle vont et viennent, pour que le champ en cours garde le clavier).
  - Ligne de bloc : deux `NSTextField` de temps (police à chasse fixe, infobulles « Début (minutes:secondes.millièmes) » / « Fin (…) »), « → », la vitesse (`CueReadability` : `Speed`, couleur `systemRed` / `systemYellow` / `secondaryLabel`, `ToolTip`), un bouton corbeille (« Supprimer ce sous-titre »), le texte du bloc (multiligne ; Maj+Entrée ou Option+Entrée : nouvelle ligne), et le message d'erreur sous les champs.
  - Ligne d'intervalle : « 00:03.000 → 00:05.000 » en italique gris, « + Ajouter » si l'intervalle dure au moins `SubtitleDocument.MinDuration` (« Ajouter un sous-titre au début de ce passage »), infobulle « Aucun sous-titre de … à …. Cliquer pour y aller. ».
  - Clic sur une ligne → `SeekRequested(start)` sans recentrer ; entrée dans un champ → `EditingStarted` puis `SeekRequested(start)` ; Entrée valide, Échap rétablit la valeur du bloc ; dans les deux cas le champ rend le clavier (`EditingEnded`). La validation (fin d'édition) appelle `CommitStart` / `CommitEnd` / `CommitText` et affiche le message renvoyé.
  - Ligne active (`ActiveChanged`) : fond `#2D3440` et barre gauche `#0A64AD` comme Windows ; recentrée sauf si la position vient d'un clic dans le panneau.
- [ ] **Step 2: CI** (job `macos-app`) : build et format verts.
- [ ] **Step 3: commit** `Show the editable subtitles beside the video on a Mac`.

### Task 6: lecteur Mac : panneau, bandeau, « Générer les sous-titres », touches, Corbeille

**Files:**
- Modify: `src/ShowTime.Mac/Video/VideoPreviewWindowController.cs`, `src/ShowTime.Mac/StatusMenu.cs`, `src/ShowTime.Mac/Composition.cs`, `src/ShowTime.Mac/AppDelegate.cs`
- Create: `src/ShowTime.Mac/Video/SeekingPlayerView.cs`

**Interfaces:**
- Consumes: `SubtitleEditor`, `TranscriptPanelView`, `SubtitlePlacement`, `PlayerSeek`, `Transcriptions`, `SubtitleFile`, `VideoSavedEventArgs.MicrophoneTrackPath` / `.Transcribe`.
- Produces: `VideoPreviewWindowController(string path, bool fullScreen, IShell shell, IFileTrash trash, SubtitleFile subtitles, Transcriptions transcriptions, TranscriptionJob? transcription, string? microphoneTrack)`.

- [ ] **Step 1: composition.** Enregistrer `SubtitleFile`, `Transcriptions`, `ITranscriber` → `WhisperTranscriber`, `IAudioExtractor` → `FfmpegAudioExtractor` comme `App.ConfigureServices` (Windows) ; retirer les commentaires « No transcription on macOS yet » ; `AppDelegate` appelle `MicrophoneTracks.DeleteLeftovers()` au démarrage, comme `App.OnStartup`.
- [ ] **Step 2: `StatusMenu.OpenVideoPreview(path, fullScreen, microphoneTrack, transcribe)`** reprend `TrayController.OpenVideoPreview` : `Enqueue` si piste et `transcribe`, sinon la piste passe au lecteur ; `Retake` utilise `Discard()`.
- [ ] **Step 3: `SeekingPlayerView : AVPlayerView`** : `KeyDown` ← → `PlayerSeek.Back`, → → `PlayerSeek.Forward(position, durée)`, Début → 0 (seek à tolérance nulle), sans modificateur ; le reste (Espace, plein écran) à `base`.
- [ ] **Step 4: fenêtre.** Un `NSSplitView` : à gauche `TranscriptPanelView` (340 points, 220 au moins, caché tant que `HasSubtitles` est faux, la fenêtre s'élargit de 340 quand il apparaît), à droite le lecteur. Dans la barre du haut, « Générer les sous-titres » (symbole `captions.bubble`) visible quand `CanGenerate`, grisé sans modèle, avec les trois infobulles de `VideoPreviewWindow.UpdateGenerateButton` ; le clic passe la piste à `Generate` (puis l'oublie) et montre le panneau. Un observateur périodique de l'`AVPlayer` (0,1 s) appelle `ShowPosition` ; `SeekRequested` déplace le lecteur ; `EditingStarted` met en pause ; `EditingEnded` rend le clavier au lecteur (`MakeFirstResponder(_playerView)`). Le bandeau : un `NSTextField` blanc sur noir translucide (alpha 0,6) dans `ContentOverlayView`, montré sur `CueShown`, placé par `SubtitlePlacement.For(bounds, PresentationSize de l'élément)` à chaque redimensionnement.
- [ ] **Step 5: ⌘Z / ⇧⌘Z.** Le contrôleur exporte `undo:` / `redo:` vers `editor.Undo()` / `Redo()` et `validateMenuItem:` (actif seulement s'il y a des sous-titres) ; pendant la saisie, le champ garde son propre Annuler (le premier répondeur passe avant).
- [ ] **Step 6: Corbeille et fermeture.** `Discard()` : après la vidéo, `editor.Close(discard: true)` puis le `.srt` (`SubtitleFile.PathFor`) à la Corbeille s'il existe, comme `TrashSubtitles` sous Windows ; fermeture normale : `editor.Close(discard: false)` et `transcriptions.DiscardTrack` d'une piste jamais utilisée.
- [ ] **Step 7: CI** verte (build, format, et la ligne `[ok]   transcription` de la tâche 1).
- [ ] **Step 8: commit** `Transcribe and edit subtitles in the Mac player`.

### Task 7: barre et réglages Mac, documentation

**Files:**
- Modify: `src/ShowTime.Mac/Recording/RecordingBar.cs`, `src/ShowTime.Mac/Recording/MacRecordingView.cs` (s'il fabrique la barre), `src/ShowTime.Mac/Settings/SettingsWindowController.cs`, `README.md`, `CLAUDE.md`, `docs/test-checklist.md`

**Interfaces:**
- Consumes: `SubtitleToggle.For` (tâche 3), `Transcriptions.CanTranscribe` (ou `TranscriptionModelLocator.Find`), `SettingsForm.TranscriptionLanguages`.

- [ ] **Step 1: bascule « Sous-titres » sur la barre**, après celles du micro et du son, avec l'état de `SubtitleToggle.For(canTranscribe, micro activé, settings.TranscribeMicrophone, verrouillé)` recalculé quand le micro change et à l'entrée en enregistrement ; un clic écrit `TranscribeMicrophone` et `TrySave` comme les deux autres bascules.
- [ ] **Step 2: section « Sous-titres » des réglages** : case « Transcrire le micro en sous-titres après l'enregistrement » avec la phrase d'aide de Windows, « Langue » (`SettingsForm.TranscriptionLanguages`), « Modèle » (champ, « Parcourir… » filtré sur `.bin`, état « Livré avec ShowTime : … » / « Modèle trouvé. » / « Modèle introuvable : les vidéos ne pourront pas être transcrites. Laissez le champ vide pour utiliser celui livré avec ShowTime. » en rouge) ; la case est grisée sans modèle sauf si elle est cochée ; `Save` passe les trois valeurs du formulaire (fin du commentaire « not on macOS yet ») ; « Valeurs par défaut » les remet.
- [ ] **Step 3: documentation.** README : sous-titres sur Mac, ←/→ de 5 s. CLAUDE.md : `SubtitleEditor` et les règles partagées (section Transcription), whisper.cpp et le modèle dans le bundle (Versioning and releases, `package-macos.sh`), plus de « The app does not transcribe yet ». Checklist : 5 s dans la partie Windows ; dans la partie Mac, la section Sous-titres de Windows adaptée (bascule, transcription après l'enregistrement, panneau, bandeau, « Générer les sous-titres », « Réessayer », ⌘Z, Supprimer / Refaire avec le `.srt`, nouvel enregistrement pendant une transcription, ←/→/Début/Espace, et ces touches pendant la saisie d'un sous-titre), plus « sur un Mac Intel, la transcription fonctionne ».
- [ ] **Step 4: CI** verte sur tous les jobs.
- [ ] **Step 5: commit** `Offer subtitles on the Mac bar and in its settings`.
