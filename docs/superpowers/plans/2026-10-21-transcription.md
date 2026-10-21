# Transcription du micro et éditeur de sous-titres synchronisé

## Contexte

L'utilisateur veut, sur choix, transcrire ce qu'il dit au micro pendant un enregistrement vidéo, puis relire et corriger
cette transcription dans la fenêtre de lecture (`VideoPreviewWindow`) : une colonne verticale à gauche de la vidéo,
bloc par bloc, texte et timecodes modifiables, toujours positionnée sur le bloc (ou l'intervalle entre deux blocs) qui
correspond à la position de lecture. Windows et macOS : tout ce qui n'est pas vue reste dans le core et
l'infrastructure portable.

Contraintes du projet qui pèsent : aucune donnée ne quitte la machine (transcription locale, aucun appel réseau dans
l'app), règles testables dans `LocalTake.Core`, nouveaux noms de fichiers et réglages = interface publique.

Décisions prises avec l'utilisateur :
- **Modèle livré avec la release** (la CI le télécharge au build, l'app ne fait toujours aucun appel réseau).
- **`.srt` à côté du MP4**, même nom de base.
- **Sauvegarde automatique** de chaque modification validée.

## Le processus en deux temps : confirmé, avec un ajustement

Transcrire *après* l'arrêt est le bon choix : Whisper est plus juste avec tout le contexte, et transcrire en direct
prendrait le CPU à x264/gdigrab pendant la capture (images perdues, décalage son/image que `SyncTests` surveille).

Ajustement : on ne ré-extrait pas le son du MP4, car il mélange micro et son de l'ordinateur (`amix`). **Le même
ffmpeg écrit une deuxième sortie** : la piste micro seule, en WAV 16 kHz mono PCM (le format d'entrée de Whisper),
dans un dossier de travail local. Même entrée, mêmes horodatages que le MP4 : les timecodes de la transcription sont
ceux de la vidéo. `q` finalise les deux fichiers. Le WAV est supprimé après la transcription.

## Moteur : Whisper.net (whisper.cpp)

- Paquets `Whisper.net` + `Whisper.net.Runtime` (MIT ; natifs win-x64, osx-arm64/x64, Metal sur Mac), versions exactes
  dans `Directory.Packages.props`. Jamais `WhisperGgmlDownloader` (appel réseau).
- Modèle livré : `ggml-small-q5_1.bin` (190 Mo). Spike (tâche 1) sur 2 min 15 de français, CPU 16 threads logiques,
  8 utilisés : small-q5_1 en 42 s, large-v3-turbo-q5_0 en 3 min 08 (plus lent que le temps réel), pour une qualité
  équivalente. Whisper.net 1.9.1 : ses bibliothèques natives doivent être dans `runtimes\win-x64` à côté de l'exe.
- Découpage en sous-titres : d'abord par Whisper (`SplitOnWord`, ~80 caractères), puis par le profil « Tutoriel » (voir la fin).
- Hallucinations connues en silence (« Sous-titres réalisés par la communauté d'Amara.org »…) : filtrées dans le core.

## Plan par tâches (chaque comportement commence par un test qui échoue)

### Tâche 0 — Plan dans le dépôt
Copier ce plan dans `docs/superpowers/plans/2026-10-21-transcription.md` (en français, comme le plan macOS).

### Tâche 1 — Spike de faisabilité (jetable, avant tout le reste)
- Whisper.net chargé depuis l'exe publié en **single-file** (`PublishSingleFile`) : les natifs se trouvent-ils ?
  Sinon `IncludeNativeLibrariesForSelfExtract` ou `RuntimeOptions` pour le chemin des bibliothèques, ou natifs à côté de
  l'exe dans le zip.
- Temps de transcription de 2 min de français sur CPU avec turbo-q5_0 et small-q5_1 → choix du modèle.

### Tâche 2 — Modèle de sous-titres (core, `src/LocalTake.Core/Transcription/`)
- `SubtitleCue(TimeSpan Start, TimeSpan End, string Text)`.
- `SrtFormat` : `Parse` tolérant (BOM, CRLF/LF, dernière ligne vide absente) et `Write` (culture invariante, `hh:mm:ss,fff`).
- `Timecode` : `Format` pour l'édition (`mm:ss.fff`, heures si besoin) et `TryParse` tolérant (`1:02.3`, `62.3`, `00:01:02,345`).
- `SubtitleDocument` : blocs triés, sans chevauchement (invariant garanti par chaque opération) ;
  - (remplacé ensuite par `SubtitleSegmenter` et le profil « Tutoriel », voir la section du même nom à la fin) ;
  - `Entries(duration)` : liste alternée `CueEntry` / `GapEntry` (intervalles de longueur nulle omis ; un seul
    intervalle couvrant toute la vidéo s'il n'y a aucun bloc) ;
  - **`Locate(position, duration)`** → le bloc `i` si `Start ≤ t < End`, sinon l'intervalle `[fin du précédent ou 0,
    début du suivant ou durée)`. C'est la règle de synchronisation, entièrement testée (avant le 1er bloc, bornes
    exactes, entre deux blocs, après le dernier, document vide) ;
  - éditions : `SetText`, `SetStart`, `SetEnd` (bornées par les voisins, durée min 0,1 s, refus motivé sinon),
    `Remove`, `AddInGap` (nouveau bloc vide dans un intervalle, 2 s max) ; chaque édition passe par `UndoHistory`
    (`Core/Editor/UndoHistory.cs`, réutilisé) ; événement `Changed`.
- `SubtitleFile` (IFileSystem) : chemin `Path.ChangeExtension(video, ".srt")`, `Load`, `Save` atomique (fichier
  temporaire puis remplacement).

### Tâche 3 — Piste micro dans la commande ffmpeg (core + intégration)
- `VideoOptions` gagne `MicrophoneTrackPath` (null par défaut).
- `RecordingCommand.Build(inputs, outputPath, microphoneTrackPath = null)` : après le MP4, ajoute
  `-map {micro}:a -ac 1 -ar 16000 -c:a pcm_s16le <wav>`. Le micro est toujours `AudioInputs[0]` sur les deux
  plateformes (`DirectShowGdigrab`, `AvFoundation`) : le documenter sur `CaptureInputs`. Sans piste micro, la commande
  est inchangée (les fixtures `tests/fixtures/legacy` doivent toujours passer).
- Test d'intégration : micro simulé par `lavfi` (bips), vérifier que les bips du WAV et du MP4 tombent au même instant
  (±50 ms), et que `q` laisse un WAV lisible.

### Tâche 4 — Port, implémentation et file de transcription
- Core, `Ports/` : `ITranscriber.TranscribeAsync(wavPath, modelPath, language, IProgress<double>, CancellationToken)`
  → segments.
- Core : `TranscriptionModelLocator` (sur le modèle de `FfmpegLocator`) : réglage, puis `models/*.bin` à côté de
  l'app, puis le dossier de données ; null si absent.
- Core : `Transcriptions` (cas d'usage, un travail à la fois, thread UI comme `RecordingSession`) :
  `Enqueue(videoPath, wavPath)` → `TranscriptionJob` (état En attente / En cours + % / Terminé / Échec / Annulé,
  événement `Changed`). Écrit le `.srt` via `SubtitleFile`, supprime le WAV. **Annule et remet en file** le travail en
  cours dès qu'un enregistrement démarre (`IRecordingStatus`, à étendre d'un événement), pour ne jamais disputer le CPU
  à une capture ; reprend quand la session revient à Idle. `Cancel(videoPath)` pour Supprimer/Refaire. Échec →
  message en français, WAV conservé pour « Réessayer ».
- Infrastructure : `WhisperTranscriber` (Whisper.net), threads = moitié des cœurs, langue `fr` ou `auto`.
- `IAppPaths` gagne `WorkFolder` (dossier de travail local des WAV, pas synchronisé), implémenté dans `AppPaths`.
- `DiagnosticsReport` : vérifie le modèle (trouvé, taille) et le chargement de la bibliothèque native.

### Tâche 5 — Réglages et session d'enregistrement
- `AppSettings` (clés nouvelles, un ancien fichier les reçoit par défaut, `SchemaVersion` inchangé) :
  `TranscribeMicrophone` (false), `TranscriptionLanguage` (`"fr"`), `TranscriptionModelPath` (`""`).
  `SettingsEditor`/`SettingsForm` + fenêtre de réglages : case, langue (Français / Détection automatique), chemin du
  modèle avec état « trouvé / introuvable ».
- `RecordingSession.LaunchAsync` : WAV demandé seulement si réglage actif **et** micro choisi **et** modèle trouvé ;
  le WAV est supprimé avec la vidéo (annulation, échec, arrêt pendant le décompte). `VideoSavedEventArgs` gagne
  `MicrophoneTrackPath`.
- `RecordingBar` : troisième bascule « Transcrire » (désactivée sans micro ou sans modèle, infobulle explicative),
  écrite directement dans les réglages comme les deux autres.

### Tâche 6 — Panneau de sous-titres dans `VideoPreviewWindow` (Windows)
- `TrayController.OpenVideoPreview` : met la transcription en file et passe le `TranscriptionJob` (ou rien) à la
  fenêtre ; sans transcription ni `.srt` existant, le panneau est masqué et la fenêtre reste telle qu'aujourd'hui.
- Nouveau `UserControl` `Recording/TranscriptPanel` ancré à gauche (~340 px, `GridSplitter`), `FitToVideo` en tient
  compte. En-tête : état (« Transcription… 42 % », « Aucune parole détectée », erreur + « Réessayer »).
- `ListBox` virtualisée des `Entries` :
  - bloc : début et fin (`Timecode`), texte multiligne, bouton supprimer ;
  - intervalle : ligne compacte « 00:12.400 → 00:15.000 · aucun sous-titre » + « Ajouter un sous-titre ».
- **Synchronisation** : à chaque tick (100 ms quand le panneau est visible), à chaque saut et pendant le glisser de la
  barre, `Locate(position)` → l'entrée active est surlignée et centrée (`ScrollIntoView` + centrage). Un clic sur
  un bloc ou un intervalle (hors champ) déplace la lecture à son début.
- **Édition** : entrer dans un champ met la lecture en pause (la position ne bouge plus, le panneau ne défile pas sous
  le curseur). Entrée / perte du focus valide via `SubtitleDocument` ; refus → bordure rouge + raison, Échap annule.
  Chaque validation réécrit le `.srt` (`SubtitleFile.Save`, différé de 500 ms) ; Ctrl+Z / Ctrl+Y hors champ.
- `Window_PreviewKeyDown` : ignore Espace, flèches, Origine, Échap quand un `TextBox` a le focus.
- `DiscardAsync` (Supprimer, Refaire) : annule le travail et envoie aussi le `.srt` à la Corbeille.

### Tâche 7 — Release et documentation
- `release.yml` : télécharge le modèle (URL Hugging Face et SHA-256 épinglés dans le workflow, contrôle obligatoire),
  publie `LocalTake-X.Y.Z-windows-x64.zip` (exe + `models/<modèle>.bin`) et l'exe seul (mise à jour sans
  re-télécharger le modèle).
- README : transcription, emplacement du modèle, `.srt` produit, aucune donnée envoyée. `CLAUDE.md` : section
  Transcription. `docs/test-checklist.md` : scénarios manuels. Plan macOS (tâche 5) : panneau AVKit sur le même core,
  modèle dans `Contents/Resources/models`.
- Version : MINOR (nouvelle fonctionnalité), puis `Release vX.Y.Z` quand l'utilisateur le décide.

## Fichiers principaux

- Nouveaux : `src/LocalTake.Core/Transcription/*`, `src/LocalTake.Core/Ports/ITranscriber.cs`,
  `src/LocalTake.Infrastructure/Transcription/WhisperTranscriber.cs`, `src/LocalTake.Windows/Recording/TranscriptPanel.xaml(.cs)`.
- Modifiés : `RecordingCommand.cs`, `CaptureInputs.cs` (doc), `VideoOptions.cs`, `RecordingSession.cs`,
  `IRecordingStatus.cs`, `FfmpegVideoRecorderFactory.cs`, `AppSettings.cs`, `SettingsEditor.cs`, `IAppPaths.cs`,
  `AppPaths.cs`, `DiagnosticsReport.cs`, `App.ConfigureServices`, `TrayController.cs`, `RecordingBar.xaml(.cs)`,
  `SettingsWindow.xaml(.cs)`, `VideoPreviewWindow.xaml(.cs)`, `Directory.Packages.props`, `release.yml`.

## Vérification

- Core : `SrtFormat`, `Timecode`, `SubtitleDocument` (Locate, éditions, annulation, normalisation), `Transcriptions`
  (faux `ITranscriber` dans `Fakes.cs`, `FakeTimeProvider` : report pendant un enregistrement, annulation, `.srt` écrit,
  WAV supprimé), `RecordingCommand`, `RecordingSession`, locator, réglages anciens. Couverture core ≥ 90 %
  (`build/check-coverage.py`).
- Intégration : alignement WAV/MP4 (ffmpeg `lavfi`) ; `WhisperTranscriber` sur un court WAV de parole française
  (fixture dans `tests/fixtures/speech/`) avec le modèle `ggml-tiny` que la CI télécharge pour les tests, test ignoré si
  absent.
- Windows (`Sta.Run`) : le panneau affiche blocs et intervalles, surligne l'entrée de la position donnée, une
  validation écrit le `.srt`, Espace dans un champ ne met pas en pause.
- `dotnet build -c Release`, `dotnet test --solution LocalTake.slnx`, `dotnet format --verify-no-changes`.
- À la main : enregistrer 1 min en parlant avec la bascule « Transcrire », vérifier le panneau pendant la lecture
  (blocs et intervalles suivis, clic sur la barre), corriger un texte et un timecode, rouvrir le `.srt` dans VLC ;
  `ffmpeg -i` sur le MP4 inchangé ; exe publié en single-file transcrit bien.

## Ajout (2026-10-21) — profil « Tutoriel » pour le découpage

Premier essai réel : un bloc de 82 caractères pendant 6,2 s, puis « Claude ! » seul. Whisper ne coupait que sur la
longueur (84 caractères). Désormais Whisper donne les mots un par un avec leurs temps (`WithMaxSegmentLength(1)`), et
`SubtitleSegmenter` (cœur) les découpe selon `SubtitleProfile.Tutorial` :

- une ligne de 42 caractères au plus (Netflix FR), visant 32 (environ 5-6 mots, l'intuition de départ, gardée en
  cible souple plutôt qu'en limite stricte) ; une seconde ligne seulement si les blocs seraient sinon trop courts ;
- de 1 à 5 s par bloc (Netflix : 0,83 à 7 s) ; un silence de 0,5 s termine un bloc ; le bloc reste 0,5 s après le
  dernier mot s'il y a de la place, en laissant 0,1 s avant le suivant ;
- coupures par ordre de préférence : fin de phrase, virgule, avant une conjonction, un relatif ou une préposition ;
  jamais après un article, une préposition ou un pronom sujet ; choix global par programmation dynamique sur le coût
  des blocs ;
- vitesse de lecture affichée par bloc dans l'éditeur : orange au-delà de 15 car./s (charte Arcom), rouge au-delà de
  17 (Netflix FR).
