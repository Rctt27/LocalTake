# App macOS au niveau de l'app Windows — conception

## Intention

L'app Mac (branche `feature/macos-app`, sortie prévue en **v0.5.0**) doit faire tout ce que fait l'app Windows v0.4.0.
Elle sera utilisée par l'utilisateur sur son Mac, puis par des collègues : elle doit donc s'installer et fonctionner
**sans manipulation technique** : on glisse l'app dans Applications (on extrait le zip sous Windows) et c'est tout. Pas
de dépendance à installer à part (ni ffmpeg, ni Homebrew, ni winget, ni pilote audio), pas d'autorisation système en
plus de celles que macOS demande d'office pour une app de capture (Enregistrement de l'écran et Micro), et pas
d'autorisation à redonner à chaque mise à jour.

Aujourd'hui, l'app Mac fait les captures, l'éditeur, la vidéo et les réglages au niveau de Windows, mais il lui manque :
les sous-titres (transcription), le surlignage à la souris, la capture du son de l'ordinateur sans pilote, et quelques
raccourcis du lecteur. Et sur les deux plateformes, ffmpeg doit aujourd'hui être installé à part : il sera livré avec
l'app. La comparaison écran par écran est résumée plus bas.

## Objectifs

1. **Sous-titres sur Mac**, à l'identique de Windows (`CLAUDE.md`, section Transcription) :
   - bascule « Sous-titres » sur la barre d'enregistrement (`TranscribeMicrophone`), avec les mêmes règles
     (désactivée sans micro, ou sans modèle, avec la même infobulle) ;
   - piste du micro écrite par le même ffmpeg (seconde sortie 16 kHz mono, `MicrophoneTracks`), transcription par
     `Transcriptions` / `WhisperTranscriber` après l'enregistrement, ou depuis « Générer les sous-titres » ;
   - dans le lecteur : panneau de sous-titres modifiables à gauche de la vidéo (blocs et intervalles, bloc actif
     centré, modification du texte et des temps, ajout dans un intervalle, Annuler / Rétablir, vitesse de lecture en
     orange / rouge), bandeau du sous-titre en cours sur l'image, bouton « Générer les sous-titres », progression,
     « Réessayer » après un échec, enregistrement du `.srt` 0,5 s après chaque modification ; « Supprimer » et
     « Refaire » annulent la transcription et envoient le `.srt` à la Corbeille avec la vidéo ;
   - section « Sous-titres » des réglages : case « Transcrire le micro en sous-titres après l'enregistrement »,
     langue (Français, Anglais, Détection automatique), modèle (champ, Parcourir…, état « trouvé / introuvable ») ;
   - pistes laissées par un arrêt brutal effacées au démarrage, comme sous Windows ;
   - whisper.cpp et le modèle `ggml-small-q5_1.bin` livrés dans l'app (le modèle dans `Contents/Resources/models`,
     où `AppPaths` le cherche déjà) ; l'app ne télécharge toujours rien ;
   - `--diagnostics` vérifie de nouveau la transcription sur Mac (`DiagnosticsReport`, `transcribes: true`).
2. **Surlignage à la souris sur Mac**, au clavier seulement : même comportement que Windows v0.4.0 (déclencheur actif
   seulement pendant l'enregistrement, traînée jaune `#FFD400` à 25 %, trait de 14,4 px à 100 % multiplié par l'échelle
   de l'écran, pause de 0,3 s puis fondu de 0,5 s), indication « Surligner : maintenir Option+Space » sur la barre,
   champ « Surligner (maintenir) » dans les réglages. Défaut macOS : **`Option+Espace`** (`Alt+Space` dans
   `settings.json`) ; Windows garde `Ctrl+Space`. La traînée apparaît dans la vidéo. macOS 15.0 et 15.1 refusaient
   les raccourcis dont les seuls modificateurs sont Option ou Option+Maj (Apple serait revenu dessus en 15.2, avec des
   signalements contraires) : un test sur le runner macOS de la CI vérifie dès la première tâche que `Option+Espace`
   s'enregistre. **S'il est refusé, le défaut macOS devient `Ctrl+Maj+Espace`** (décision de l'utilisateur), et le
   champ des réglages Mac refuse les combinaisons que macOS rejette, avec un message.
3. **Son du Mac sans pilote** : LocalTake capte lui-même ce que joue le Mac (ScreenCaptureKit, son de l'écran
   enregistré) et le passe à ffmpeg par un tuyau (FIFO), comme `WasapiSystemAudio` sous Windows. Même exigence de
   synchronisation que Windows (son et image à ±0,1 s), même règle de mélange avec le micro (`amix`), même continuité
   (le flux garde sa durée quand rien ne joue). BlackHole n'est plus nécessaire ; s'il est installé, il reste écarté de
   la liste des micros.
4. **Lecteur** : ←/→ reculent / avancent de **5 s** (au lieu de 10 s aujourd'hui sous Windows : les vidéos font
   rarement plus de 2 ou 3 minutes, décision de l'utilisateur), Début revient au début, Espace lit / met en pause.
   - Mac : dans le lecteur natif d'Apple (`AVPlayerView`) ; les touches sont ignorées quand un champ du panneau de
     sous-titres a le clavier.
   - Windows : le pas passe de 10 s à 5 s, infobulles comprises (« Reculer de 5 secondes (←) », « Avancer de 5 secondes
     (→) »). C'est le seul changement voulu de l'app Windows en dehors de ffmpeg.
5. **ffmpeg livré avec l'app, sur Mac et sur Windows.** L'utilisateur n'installe plus ffmpeg.
   - Mac : un ffmpeg universel (arm64 et x86_64) dans le bundle, signé avec l'app, trouvé en premier par
     `AppPaths.CreateFfmpegLocator`.
   - Windows : `ffmpeg.exe` dans le zip, à côté de `LocalTake.exe` (le localisateur le cherche déjà là en premier).
   - Le binaire vient d'une version épinglée, vérifiée par SHA-256 au build (CI et release), comme le modèle Whisper ;
     l'app ne télécharge toujours rien. Il doit avoir ce dont LocalTake se sert : libx264, `avfoundation` (Mac),
     `gdigrab` et `dshow` (Windows), l'entrée PCM brute par tuyau, le WAV 16 kHz de la piste micro.
   - Licence : ffmpeg avec x264 est sous GPL ; il est livré comme programme séparé, que LocalTake lance. Chaque
     release joint sa licence et l'adresse de son code source exact (version épinglée).
   - Le réglage « ffmpeg » des réglages reste : vide = celui livré avec l'app ; un chemin = un autre ffmpeg.
   - Les messages et le README qui disent d'installer ffmpeg (`InstallHint`, « ffmpeg est introuvable… ») changent en
     conséquence ; « introuvable » devient une anomalie d'installation.
6. **Mises à jour sans réautorisation sur Mac** : l'app est signée avec un certificat auto-signé stable (gratuit, sans
   compte Apple), gardé dans les secrets GitHub et utilisé par les jobs `macos-app` (CI) et `macos` (release), afin que
   macOS garde les autorisations Enregistrement de l'écran et Micro d'une version à l'autre. L'utilisateur crée le
   certificat une fois, guidé pas à pas ; sans le secret (fork, PR externe), la CI retombe sur la signature ad hoc.
   La toute première ouverture demande toujours « Ouvrir quand même » (pas de notarisation).

## Non-objectifs

- Boutons latéraux de souris comme déclencheur de surlignage sur Mac (ils ne peuvent pas être interceptés sans
  l'autorisation Accessibilité). Le champ des réglages Mac n'accepte que le clavier. Un bouton de souris peut être
  associé à une touche (F13–F24) dans le logiciel du fabricant.
- Signature Developer ID et notarisation (un compte Apple Developer payant) : la première ouverture reste à confirmer
  dans Réglages Système.
- Refaire le lecteur Mac à l'image de celui de Windows : on garde `AVPlayerView`.
- L'aperçu d'image au survol de la barre de lecture (fourni ou non par `AVPlayerView` ; pas d'implémentation propre).

## Anti-objectifs (échec même si « ça marche »)

- Toute demande d'autorisation **Accessibilité** ou **Surveillance de l'entrée** (Input Monitoring).
- Quoi que ce soit à installer à côté de l'app : ffmpeg, Homebrew, winget, pilote audio, modèle.
- Des autorisations à redonner après une mise à jour.
- Un appel réseau, un téléchargement par l'app, une capture qui quitte la machine.
- Une régression de l'app Windows (comportement, réglages, fichiers produits), en dehors des deux changements voulus :
  ffmpeg livré dans le zip et le pas de 5 s du lecteur.
- Un `settings.json` existant qui ne se charge plus.

## Contraintes

- **Tests en local : jamais sans l'accord explicite de l'utilisateur** (ils font planter une autre application sur son
  PC Windows). Quand des tests doivent tourner en local, le travail s'arrête et attend son go. La CI distante peut
  tourner librement.
- Pas de Mac pour développer : l'app Mac ne se compile que sur la CI macOS (job `macos-app`), qui ne se lance sur la
  branche que via `gh workflow run CI --ref feature/macos-app` (ou une PR). L'app Mac n'a pas de tests automatisés :
  la logique testable va dans le cœur ou dans `LocalTake.Platform.MacOS` (testé sur le runner macOS), et l'app se
  vérifie à la main avec la partie macOS de `docs/test-checklist.md`, sur l'artefact `LocalTake-macos`.
- Les runners macOS n'ont pas l'autorisation Enregistrement de l'écran : ScreenCaptureKit (images et son) ne s'y teste
  pas ; la synchronisation son/image sur Mac se vérifie à la main (procédure écrite dans la checklist).
- macOS 14 minimum, app universelle (Apple silicon et Intel), trimmée (`TrimMode` partial, un avertissement de trim
  fait échouer le build).
- Règles du projet (`CLAUDE.md`) : textes d'interface en français, code en anglais, cœur testable à 90 %, analyzers en
  erreur, pixels physiques partout, `settings.json` interface publique.

## Décisions déjà prises

- Utilisateurs : l'utilisateur puis des collègues, sur leurs propres Mac.
- Pas d'autorisation Accessibilité ; surlignage au clavier seulement sur Mac.
- Défaut du surlignage sur Mac : `Option+Espace` ; repli `Ctrl+Maj+Espace` si macOS refuse `Option+Espace`.
- ffmpeg livré avec l'app sur Mac et sur Windows ; plus rien à installer à part.
- Certificat auto-signé stable pour signer l'app Mac.
- Son du Mac : capté par LocalTake (ScreenCaptureKit), plus de BlackHole.
- Sous-titres sur Mac avec Whisper (whisper.cpp), modèle livré dans l'app.

## Choix de l'implémentation (mes choix, à vérifier par l'utilisateur)

- **Whisper sur Mac** : on garde les bibliothèques natives `macos-arm64` et `macos-x64` du paquet `Whisper.net.Runtime`
  et on retire celles des autres plateformes (le SDK macOS échoue sur les `.so` Linux) ; le réglage actuel
  `WhisperRuntimes=false` devient « macOS seulement ». Les fonctions `__Internal` de Whisper.net restent hors de
  l'édition de liens (`ReferenceNativeSymbol` … `Ignore`, déjà en place) : sur macOS, Whisper.net charge
  `libwhisper.dylib` à l'exécution. Le point à vérifier en premier, sur la CI puis sur un Mac : Whisper.net trouve la
  bibliothèque de la bonne architecture depuis le bundle (sinon : la placer là où il la cherche, ou lui indiquer le
  chemin).
- **Son du Mac** : la capture ScreenCaptureKit (`SCStream`, `capturesAudio`, sans vidéo utile) vit dans l'app Mac ;
  le tuyau FIFO, l'écriture du PCM, le comblement par du silence quand rien ne joue ou quand la capture s'arrête
  (`SilencePadding`) et l'ordre des entrées ffmpeg (micro, écran, puis tuyau : `CaptureInputs.TrailingAudioInputs`)
  vont dans le cœur et `LocalTake.Platform.MacOS`, testés. `AvFoundation` implémente `CanRecordSystemAudio` et
  `BuildInputs(…, PipedAudio?)` comme `DirectShowGdigrab`.
- **Surlignage** : `MacHighlightInput` (`LocalTake.Platform.MacOS` pour ce qui est testable) : appui et relâchement par
  Carbon (déjà en place dans `MacHotkeys`), état tenu touche et modificateurs compris, position de la souris convertie
  en pixels par `MacScreenSpace`, sans aucune autorisation (pas d'event tap). Overlay : une `NSWindow` sans bordure,
  transparente, qui ignore la souris, au-dessus des fenêtres, `sharingType` par défaut (capturée par ffmpeg), une par
  écran couvert par la zone si nécessaire.
- **Interface** : AppKit natif, en suivant les vues Windows pour le contenu et le comportement, et les conventions Mac
  pour la forme (⌘Z / ⇧⌘Z dans le panneau, Finder au lieu d'Explorateur). Le panneau de sous-titres reprend tel quel
  le modèle et les règles du cœur (`SubtitleDocument`, `Timeline`, `SubtitleProfile.Check`, `UndoHistory`) ; seules
  les vues sont nouvelles.
- **Réglages** : `SettingsDefaults.ForMacOS` passe `HighlightTrigger` à `Alt+Space` (l'app Mac n'est pas encore sortie :
  aucun fichier Mac existant à préserver).

## Laissé au constructeur

- Le découpage en tâches et l'ordre (le son du Mac est le plus risqué ; les sous-titres le plus gros).
- La forme exacte des vues AppKit, dans le respect du contenu ci-dessus.
- Les procédures de vérification manuelle à ajouter à `docs/test-checklist.md` (synchronisation son / image sur Mac,
  sous-titres, surlignage dans la vidéo, installation sur un Mac « neuf »).
- La mise à jour de `README.md` et `CLAUDE.md` (plus de BlackHole ni d'installation de ffmpeg, sous-titres et
  surlignage sur Mac, certificat).
- D'où vient le ffmpeg épinglé de chaque plateforme (build reconnu et vérifiable, ou compilé dans la CI), dans le
  respect des exigences du point 5.

## Comparaison des deux apps (état au 2026-10-24)

| Domaine | Windows v0.4.0 | Mac (branche) |
| --- | --- | --- |
| Menu (barre des tâches / barre des menus) | complet | équivalent |
| Captures (zone, fenêtre, écran, dernière zone, ouvrir, coller) | oui | oui |
| Éditeur (outils, raccourcis, zoom, enregistrer, copier) | oui | oui |
| Barre d'enregistrement | micro, son du PC, sous-titres, surlignage | micro, son du Mac (BlackHole) |
| Son de l'ordinateur | capté sans pilote (WASAPI) | BlackHole + périphérique à sorties multiples |
| Sous-titres | oui | non |
| Surlignage | oui | non |
| Lecteur | ±10 s (passera à ±5 s), Début, Espace, aperçu au survol, Supprimer, Refaire, Explorateur | AVPlayerView, Supprimer, Refaire, Finder |
| Réglages | + sous-titres, + surlignage | sans sous-titres ni surlignage |
| Lancement à l'ouverture de session | oui | oui |
| ffmpeg | à installer (winget) | à installer (Homebrew) |
| Signature | — | ad hoc : autorisations à redonner à chaque version |
