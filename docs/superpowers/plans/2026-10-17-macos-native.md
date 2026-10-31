# ShowTime macOS natif — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** une vraie app Mac en C# (barre des menus, capture, éditeur, vidéo avec lecteur intégré, réglages) sur le cœur commun, livrée en v0.5.0 (la v0.4.0 est sortie pour Windows seul, avec le surlignage à la souris).

**Architecture :**
- `ShowTime.Mac` (`net10.0-macos`, AppKit et AVKit) ne contient que des vues et des adaptateurs écrits avec les bindings Apple de .NET.
- `ShowTime.Platform.MacOS` (`net10.0`, testable sur n'importe quel runner) garde ce que les bindings ne couvrent pas :
  - les raccourcis Carbon ;
  - la géométrie des écrans, en points et pixels, avec l'origine Cocoa en bas à gauche.
- La logique reste dans `ShowTime.Core` et l'enregistrement passe par ffmpeg (`AvFoundation`, déjà testé).

**Tech stack :**
- .NET 10 et le workload `macos` (Xcode 26.x requis, version exacte imposée par le workload) ;
- AppKit, AVKit, ScreenCaptureKit, ServiceManagement (`SMAppService`), UserNotifications ;
- ffmpeg avfoundation ; BlackHole 2ch pour le son de l'ordinateur.

**Spec :** `C:\Users\FlowUP\.claude\plans\j-aimerais-profesionnaliser-la-structure-vivid-fog.md` (plan d'ensemble, section « Plans détaillés », point 2).

## Global Constraints

- **Branche :** `feature/macos-app`, créée depuis `main` une fois la v0.2.0 fusionnée.
- **Aucun appel réseau, aucune télémétrie :** les notifications sont locales (`UNUserNotificationCenter`), rien n'est envoyé.
- **Langues :** chaînes visibles en français ; code, commentaires et commits en anglais.
- **macOS minimum :** 14.0 (`SupportedOSPlatformVersion`).
- **Puces :** `RuntimeIdentifiers` = `osx-arm64;osx-x64`, soit un seul `.app` universel.
- **Identité de l'app :**
  - bundle `io.github.rctt27.showtime` ; nom affiché `ShowTime` ; exécutable `ShowTime` ;
  - `LSUIElement=true` (pas d'icône dans le Dock) ;
  - `NSMicrophoneUsageDescription` = « ShowTime enregistre le micro dans vos vidéos, uniquement sur cet ordinateur. ».
- **Réglages :** `~/Library/Application Support/ShowTime/settings.json`, valeurs par défaut `SettingsDefaults.ForMacOS`. Les raccourcis par défaut (`Ctrl+Maj+1/2/3/4`, `Ctrl+Maj+R/D`) s'affichent avec `HotkeyNaming.MacOS`.
- **Signature :** ad hoc (`codesign --sign -`). Ni notarisation ni Developer ID tant que l'utilisateur n'a pas décidé (voir le plan d'ensemble).
- **Solutions :** `ShowTime.slnx` n'inclut pas `ShowTime.Mac` (il doit se compiler sous Windows) ; `ShowTime.Mac.slnx` = Core, Infrastructure, Platform.MacOS, `ShowTime.Mac` et leurs tests.
- **Build :** `TreatWarningsAsErrors` et analyzers comme le reste.
- **Tests automatiques :** l'app Mac n'en a pas, car il n'y a pas d'hôte de test `net10.0-macos` fiable en CI. Toute logique qui mérite un test va donc dans Core ou `Platform.MacOS`. La vérification fonctionnelle passe par la checklist de chaque tâche, déroulée par l'utilisateur sur son Mac avec l'artefact CI.
- **Push et PR :** chaque artefact exige un push. On demande l'accord de l'utilisateur une fois, au début du plan, pour pousser la branche `feature/macos-app` à chaque tâche (dépôt public).

## Review Focus

1. **Autorisation « Enregistrement de l'écran » absente ou refusée :** au lieu d'une capture noire ou réduite au fond d'écran, un message en français dit où l'accorder (Réglages Système > Confidentialité et sécurité > Enregistrement de l'écran), puis ShowTime se relance. Pris en charge par la tâche 2.
2. **Deux écrans d'échelles différentes** (Retina 2× et écran externe 1×) : chaque capture a la taille en pixels de son écran, et les zones des écrans ne se chevauchent pas dans l'espace global. Tests dans la tâche 2.
3. **Écran secondaire en vidéo :** l'index « Capture screen N » d'ffmpeg peut ne pas suivre l'ordre de `NSScreen.Screens`. Le diagnostic affiche les deux listes, et la tâche 5 vérifie la vidéo sur le second écran.
4. **Raccourci déjà pris par une autre app :** un avertissement liste les raccourcis refusés, comme sous Windows (comportement du cœur). Point de checklist de la tâche 3.
5. **Quitter avec un éditeur non enregistré** (menu « Quitter » ou `Cmd+Q` dans l'éditeur) : la question « Enregistrer ? » est posée, et « Annuler » garde l'app ouverte (`ApplicationShouldTerminate`). Point de checklist de la tâche 4.

---

### Task 1: Chaîne de build, app minimale et prérequis du Mac

**Files:**
- Create :
  - `ShowTime.Mac.slnx` ;
  - `src/ShowTime.Mac/ShowTime.Mac.csproj` ;
  - `src/ShowTime.Mac/Main.cs` ;
  - `src/ShowTime.Mac/AppDelegate.cs` ;
  - `src/ShowTime.Mac/Composition.cs` ;
  - `src/ShowTime.Mac/Info.plist` ;
  - `src/ShowTime.Mac/Resources/` : icône de l'app en catalogue `AppIcon.appiconset`, généré depuis `ShowTime.ico` ; icône de barre des menus `StatusIcon.png` et `StatusIcon@2x.png` en modèle monochrome (*template image*) de 18 pt.
- Modify :
  - `.github/workflows/ci.yml` : job `macos-app` ;
  - `docs/test-checklist.md` : section macOS réécrite, une sous-section par tâche.

**Interfaces:**
- Consumes :
  - `DiagnosticsReport` (Infrastructure) ;
  - `MacHotkeys` et `MacScreenGrabber`, ceux existants de `Platform.MacOS` (le second est remplacé à la tâche 2) ;
  - `AppPaths.ForCurrentPlatform()` et `SettingsDefaults.ForMacOS`.
- Produces :
  - `static class Composition { public static ServiceProvider Build(); }`, la racine de composition Mac, enrichie à chaque tâche ;
  - `AppDelegate : NSApplicationDelegate`, propriétaire de `NSStatusItem`.

- [ ] **Step 1 : projet `net10.0-macos` et `Info.plist`**

Le projet reprend les valeurs des Global Constraints, ainsi que `ApplicationDisplayVersion` = `$(Version)` et `SupportedOSPlatformVersion` 14.0.

- [ ] **Step 2 : `Main.cs` et `AppDelegate`**

- Avec `--diagnostics` : résoudre `DiagnosticsReport` et écrire sur `Console.Out` et dans `<logs>/diagnostics.txt`, puis quitter avec son code.
- Sinon : `NSApplication.Init()`, puis `ActivationPolicy = Accessory` et `Run()`.
- Le menu de l'icône contient « ShowTime <version> » (désactivé), un séparateur et « Quitter ShowTime ».
- Une seconde instance est détectée avec `NSRunningApplication.GetRunningApplications(bundleId)`. Si une autre instance existe, l'app quitte et ne crée pas d'icône.

- [ ] **Step 3 : job CI `macos-app`**

Sur `runs-on: macos-26` :
1. checkout ;
2. `setup-dotnet` (avec `global.json`) ;
3. `sudo xcode-select -s /Applications/Xcode_<version exigée>.app`, puis `xcodebuild -version` ;
4. `dotnet workload install macos` ;
5. `dotnet format ShowTime.Mac.slnx --verify-no-changes` ;
6. `dotnet build src/ShowTime.Mac -c Release` ;
7. `codesign --force --deep --sign - <app>` (si le SDK ne signe pas déjà en ad hoc) ;
8. `ditto -c -k --keepParent <app> dist/ShowTime-macos.zip` ;
9. `upload-artifact` sous le nom `ShowTime-macos`.

La version de Xcode est celle qu'exige le workload : c'est le message d'erreur du premier build qui la donne. On la fige ensuite dans le workflow.

- [ ] **Step 4 : vérifier**

Push avec l'accord de l'utilisateur.
Attendu : le job `macos-app` est vert, l'artefact contient `ShowTime.app`, et `lipo -archs ShowTime.app/Contents/MacOS/ShowTime` affiche `x86_64 arm64`.

- [ ] **Step 5 : checklist utilisateur, tâche 1**

- Version de macOS ≥ 14.
- L'app s'ouvre par clic droit > Ouvrir.
- L'icône est dans la barre des menus, pas dans le Dock ; « Quitter » ferme l'app.
- Un second lancement ne crée pas de seconde icône.
- `brew install ffmpeg` est possible.
- `ShowTime.app/Contents/MacOS/ShowTime --diagnostics` produit un rapport, à renvoyer.

**Si un prérequis est bloqué par la politique de l'entreprise, on s'arrête là et on en reparle.**

- [ ] **Step 6 : commit** — `Add the macOS app shell: menu bar icon, diagnostics, CI build`

### Task 2: Adaptateurs Mac sur les API d'Apple, et géométrie des écrans

**Files:**
- Create :
  - `src/ShowTime.Platform.MacOS/MacScreenSpace.cs` ;
  - `src/ShowTime.Mac/Platform/MacScreenGrabber.cs` (ScreenCaptureKit) ;
  - `src/ShowTime.Mac/Platform/MacNativeWindows.cs` ;
  - `src/ShowTime.Mac/Platform/MacClipboard.cs` ;
  - `src/ShowTime.Mac/Platform/MacShell.cs` (avec `MacFileTrash`) ;
  - `src/ShowTime.Mac/Platform/MacStartupRegistration.cs` ;
  - `src/ShowTime.Mac/Adapters/MacImage.cs` ;
  - `src/ShowTime.Mac/Adapters/MacImageCodec.cs`.
- Delete :
  - de `Platform.MacOS` : `MacScreenGrabber.cs`, `MacServices.cs`, et les parties ObjC et ImageIO de `Native.cs`. Il reste Carbon, `MacHotkeys` et `MacKeys` ;
  - les tests correspondants dans `tests/ShowTime.Platform.Tests/MacOS/`.
- Test : `tests/ShowTime.Platform.Tests/MacOS/MacScreenSpaceTests.cs` (logique pure, exécutée sur tous les runners).

**Interfaces:**
- Produces :
  - `public sealed record ScreenFrame(double X, double Y, double Width, double Height, double Scale)` : frame Cocoa en points, origine en bas à gauche de l'écran principal ;
  - `public static class MacScreenSpace`, avec :
    - `public static IReadOnlyList<PixelRect> Layout(IReadOnlyList<ScreenFrame> screens)` : écran principal à l'index 0 ; Y retourné vers le bas ; origine = points × plus grande échelle ; taille = points × échelle propre de l'écran, donc jamais de chevauchement ;
    - `public static PixelPoint ToPixel(ScreenFrame screen, PixelRect laidOut, double pointX, double pointY)`.
  - Les adaptateurs implémentent les ports existants :
    - `IScreenGrabber` : `SCScreenshotManager` par écran, assemblé selon `Layout` ; `VisibleWindows` via `SCShareableContent.Windows`, à la couche 0 et hors ShowTime ;
    - `INativeWindows` : `SharingType = None` ; `Level = NSWindowLevel.ScreenSaver` ;
    - `INativeClipboard` : `NSPasteboard`, PNG et TIFF ;
    - `IShell` : `NSWorkspace.ActivateFileViewer` et `OpenUrl` ;
    - `IFileTrash` : `NSFileManager.TrashItem` ;
    - `IStartupRegistration` : `SMAppService.MainApp` ;
    - `IImageCodec`, avec `MacImage : IImage` qui enveloppe une `CGImage` ;
    - conversions `MacImage.FromPixels(PixelBuffer)` et `MacImage.ToPixels(CGImage)`, en BGRA non prémultiplié.

- [ ] **Step 1 : écrire les tests qui échouent** (`MacScreenSpaceTests`)

- `Single_retina_screen` : `(0,0,1512,982, scale 2)` → `PixelRect(0,0,3024,1964)`.
- `Screen_above_the_main_one_has_a_negative_Y` : principal `(0,0,1440,900,2)`, secondaire `(0,900,1920,1080,1)` → secondaire `PixelRect(0,-2160,1920,1080)`.
- `Mixed_scales_never_overlap` : principal `(0,0,1440,900,2)`, secondaire à droite `(1440,0,1920,1080,1)` → secondaire `X == 2880`, `Width == 1920`, et `Intersect` avec le principal est vide.
- `Point_to_pixel_uses_the_screen_scale` : sur le principal 2×, le point `(100, 850)` (Cocoa) → `PixelPoint(200, 100)`.

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test --project tests/ShowTime.Platform.Tests -c Release`
Attendu : la compilation échoue.

- [ ] **Step 3 : implémenter `MacScreenSpace`, puis les adaptateurs**

Si ScreenCaptureKit refuse faute d'autorisation, `MacScreenGrabber.Grab` lève une `UnauthorizedAccessException` avec ce message exact : « ShowTime n'a pas l'autorisation d'enregistrer l'écran. Ouvrez Réglages Système > Confidentialité et sécurité > Enregistrement de l'écran, cochez ShowTime, puis relancez-le. » Le diagnostic et `CaptureWorkflow` l'affichent tel quel (Review Focus 1). Avant la première capture, l'app appelle `CGRequestScreenCaptureAccess()` pour que macOS propose l'autorisation.

- [ ] **Step 4 : vérifier**

Run : `dotnet test --project tests/ShowTime.Platform.Tests -c Release` sous Windows, puis la CI.
Attendu : tout est vert sur les trois OS, et le job `macos-app` est vert.

- [ ] **Step 5 : checklist utilisateur, tâche 2**

- `--diagnostics` : « screen capture » donne la taille en pixels, avec plusieurs couleurs.
- Sans l'autorisation, le message en français s'affiche.
- Avec un second écran, le rapport liste les deux.

- [ ] **Step 6 : commit** — `Use Apple's APIs for capture, clipboard, trash and login item`

### Task 3: Captures depuis la barre des menus et les raccourcis

**Files:**
- Create :
  - `src/ShowTime.Mac/Capture/SelectionOverlay.cs` : une fenêtre sans bordure par `NSScreen`, au niveau `ScreenSaver`, qui couvre aussi la barre des menus et le Dock ;
  - `src/ShowTime.Mac/Capture/MacScreen.cs` (`IScreen`, `IAreaPicker`) ;
  - `src/ShowTime.Mac/StatusMenu.cs` ;
  - `src/ShowTime.Mac/Adapters/MacDialogs.cs` (`NSAlert`, `NSSavePanel`) ;
  - `src/ShowTime.Mac/Adapters/MacNotifications.cs` ;
  - `src/ShowTime.Mac/Adapters/MainThread.cs`.
- Modify : `Composition.cs`, `AppDelegate.cs`.

**Interfaces:**
- Consumes :
  - `CaptureWorkflow`, `CaptureDelivery`, `SelectionModel` ;
  - `IGlobalHotkeys` (`MacHotkeys`), `HotkeyNaming.MacOS` ;
  - `MacScreenSpace.Layout` et `ToPixel`.
- Produces :
  - `MainThread : IUiThread` (`NSApplication.SharedApplication.InvokeOnMainThread` / `BeginInvokeOnMainThread`) ;
  - `MacDialogs : IUserDialogs` (`AskYesNoCancelAsync` = `NSAlert` à trois boutons « Enregistrer », « Ne pas enregistrer », « Annuler ») ;
  - `MacNotifications : INotifications` : `UNUserNotificationCenter`, autorisation demandée au premier envoi ; un clic sur la notification montre le fichier dans le Finder ; sans autorisation, l'échec est journalisé et rien d'autre ne se passe ;
  - `StatusMenu` : les mêmes entrées et la même logique que `TrayController` sous Windows, les raccourcis affichés comme *key equivalents* du menu ; « Réglages… » est désactivé jusqu'à la tâche 6.

**Choix arrêtés :**
- Une sélection ne déborde jamais de l'écran où elle a commencé, comme l'outil de capture de macOS.
- La « dernière zone » mémorise l'écran (par `NSScreen.DeviceDescription["NSScreenNumber"]`) en plus du rectangle.
- Tant que la tâche 4 n'est pas faite, « Ouvrir dans l'éditeur » enregistre le fichier (`IEditorLauncher` temporaire, à supprimer à la tâche 4).

- [ ] **Step 1 : implémenter l'overlay**

Il reprend le comportement de Windows :
- l'image figée assombrie, la zone claire avec sa taille en pixels ;
- `Échap` annule ;
- un clic prend la fenêtre sous le curseur ;
- `Espace` bascule en mode fenêtre.

- [ ] **Step 2 : implémenter le menu, les raccourcis et la remise de la capture**

Les quatre réglages « Après une capture » sont couverts.

- [ ] **Step 3 : vérifier** — `macos-app` est vert ; tous les tests de `ShowTime.Mac.slnx` passent en CI.

- [ ] **Step 4 : checklist utilisateur, tâche 3**

- `Ctrl+Maj+1` (zone) : l'overlay couvre la barre des menus et le Dock.
- `Ctrl+Maj+2` (fenêtre), `Ctrl+Maj+3` (écran entier), `Ctrl+Maj+4` (dernière zone).
- Retina : la capture est nette et a le double des points.
- Second écran : capture dessus.
- Fichier enregistré et notification ; un clic sur la notification ouvre le Finder sur le fichier.
- Presse-papiers : collage dans Aperçu.
- Un raccourci déjà pris est signalé (Review Focus 4).

- [ ] **Step 5 : commit** — `Capture areas, windows and screens on macOS`

### Task 4: Éditeur

**Files:**
- Create :
  - `src/ShowTime.Mac/Editor/EditorWindowController.cs` ;
  - `src/ShowTime.Mac/Editor/AnnotationView.cs` (`NSView` retournée, origine en haut à gauche, en pixels de l'image) ;
  - `src/ShowTime.Mac/Editor/Annotations.cs` (les annotations à dessiner, en `record`s) ;
  - `src/ShowTime.Mac/Editor/EditorToolbar.cs`.
- Delete : le `IEditorLauncher` temporaire de la tâche 3.

**Interfaces:**
- Consumes :
  - `EditorGeometry` (`Snap45`, `RectBetween`, `Arrow`, `PixelationBlock`, `PixelationGrid`, `NextStepNumber`, `NeedsDarkText`, `ClampZoom`, `FitZoom`) ;
  - `UndoHistory`, `PixelBuffer.Pixelate` / `Crop` ;
  - `ImageSaver`, `INativeClipboard`.
- Produces :
  - `EditorWindowController.Open(IImage image)` ;
  - `internal PixelBuffer RenderImage()` : dessine l'image et les annotations dans un `CGBitmapContext` à la taille de l'image, quel que soit le zoom.

**Choix arrêtés :**
- Les mêmes outils et touches que l'éditeur Windows, `Cmd` à la place de `Ctrl` : `Cmd+S`, `Cmd+Maj+S`, `Cmd+C`, `Cmd+Z`.
- Le zoom passe par `NSScrollView.Magnification`.
- Le flou est `PixelBuffer.Pixelate`, à l'identique de Windows.

- [ ] **Step 1 : implémenter la vue, les outils, l'annulation, le recadrage et l'export**

- [ ] **Step 2 : ajouter « Ouvrir une image… » et « Coller depuis le presse-papiers » au menu**

- [ ] **Step 3 : faire poser la question à la fermeture et à la sortie**

À la fermeture comme à la sortie de l'app, un éditeur non enregistré pose la question (`ApplicationShouldTerminate`, Review Focus 5).

- [ ] **Step 4 : vérifier** — `macos-app` est vert.

- [ ] **Step 5 : checklist utilisateur, tâche 4**

- Chaque outil, sa touche, `Maj` pour contraindre.
- Recadrer puis `Cmd+Z`.
- Flou à l'export.
- `Cmd+S` en PNG et en JPEG ; `Cmd+C` puis collage dans Aperçu, à la taille de la capture quel que soit le zoom.
- Fermer ou quitter sans enregistrer pose la question.

- [ ] **Step 6 : commit** — `Add the annotation editor on macOS`

### Task 5: Vidéo — barre, cadre, enregistrement et lecteur intégré

**Files:**
- Create :
  - `src/ShowTime.Mac/Recording/RecordingBar.cs` (`NSPanel`) ;
  - `src/ShowTime.Mac/Recording/RecordingFrame.cs` (quatre fenêtres hors de la zone enregistrée) ;
  - `src/ShowTime.Mac/Recording/MacRecordingView.cs` ;
  - `src/ShowTime.Mac/Video/VideoPreviewWindowController.cs` (`AVPlayerView`).
- Create : `src/ShowTime.Core/Ports/IDisplays.cs`, `src/ShowTime.Mac/Platform/MacDisplays.cs`.
- Modify :
  - `Composition.cs` : `AvFoundation` comme `IFfmpegPlatform`, avec `() => displays.Screens()` comme liste d'écrans ;
  - `src/ShowTime.Infrastructure/Diagnostics/DiagnosticsReport.cs` : nouveau paramètre optionnel `IDisplays? displays = null` ; s'il est fourni, une ligne « screens » liste les écrans dans l'ordre qu'utilise l'enregistrement (Review Focus 3). Sous Windows, `IDisplays` n'est pas enregistré et la ligne n'apparaît pas.
- Test : `tests/ShowTime.Infrastructure.IntegrationTests/Diagnostics/DiagnosticsReportTests.cs` (cas `Screens_are_listed` et `No_screen_line_without_displays`).

**Interfaces:**
- Consumes :
  - `RecordingSession` (machine à états du cœur) ;
  - `IRecordingView`, `IRecordingViewFactory` ;
  - `RecordingBarPlacement`, `INativeWindows`, `IFileTrash`, `IShell`.
- Produces :
  - `public interface IDisplays { IReadOnlyList<PixelRect> Screens(); }` (Core) : écran principal en premier, en pixels de l'espace global ;
  - `MacDisplays : IDisplays`, qui renvoie `MacScreenSpace.Layout` des `NSScreen.Screens` ;
  - `MacRecordingViewFactory : IRecordingViewFactory`.

**Choix arrêtés :**
- Le cadre et la barre sont exclus de la capture (`SharingType = None`) **et** placés hors de la zone enregistrée. Ainsi, même si avfoundation ignore `SharingType`, ils n'apparaissent pas dans la vidéo.
- Avant le premier enregistrement avec micro, l'app appelle `AVCaptureDevice.RequestAccessForMediaType(AVMediaTypes.Audio)`.
- L'aperçu propose :
  - la lecture dans la fenêtre (`AVPlayerView`, contrôles standards de macOS, miniatures au survol fournies par AVKit) ;
  - « Afficher dans le Finder » ;
  - « Supprimer » : le lecteur est mis en pause et libéré (`Player = null`) avant `IFileTrash` ;
  - « Refaire ».
- Sous-titres (ajouté le 2026-10-21, voir `2026-10-21-transcription.md`) : le cœur et l'infrastructure les font déjà
  (`Transcriptions`, `SubtitleDocument`, `WhisperTranscriber`, piste du micro écrite par ffmpeg, `AvFoundation` met le
  micro en premier). Le Mac ajoute :
  - la bascule « Sous-titres » sur la barre (`MicrophoneTracks.CanTranscribe`) et ses réglages ;
  - à gauche de l'`AVPlayerView`, le panneau des sous-titres, sur le modèle de `TranscriptPanel` : lignes des blocs et
    des intervalles (`SubtitleDocument.Timeline`), ligne active selon `IndexAt` mise à jour par un
    `AddPeriodicTimeObserver` de 0,1 s et à chaque saut, centrée sauf après un clic dans le panneau ; un champ en cours
    d'édition met la lecture en pause ;
  - le modèle dans `Contents/Resources/models` (déjà cherché par `AppPaths`), et `runtimes/macos-arm64` de Whisper.net
    dans le bundle (le chargeur de Whisper.net les cherche à côté des assemblies : à vérifier dans le bundle signé).

- [ ] **Step 1 : écrire les tests qui échouent**

- `Screens_are_listed` : avec un `IDisplays` factice qui renvoie `PixelRect(0,0,3024,1964)` et `PixelRect(2880,0,1920,1080)`, la sortie contient `screens: 0 = 3024×1964 at 0,0; 1 = 1920×1080 at 2880,0`.
- `No_screen_line_without_displays` : sans `IDisplays`, la sortie ne contient pas `screens:`.

La checklist demande aussi à l'utilisateur de coller la sortie de `ffmpeg -f avfoundation -list_devices true -i ""`, pour comparer l'ordre des écrans.

- [ ] **Step 2 : vérifier l'échec, implémenter, vérifier le succès**

Run : `dotnet test --project tests/ShowTime.Infrastructure.IntegrationTests -c Release`

- [ ] **Step 3 : implémenter la barre, le cadre, la vue et le lecteur** — `macos-app` est vert.

- [ ] **Step 4 : checklist utilisateur, tâche 5**

- Vidéo d'une zone : autorisation Micro demandée, son présent.
- Avec BlackHole : le son de l'ordinateur est présent.
- Le cadre et la barre sont absents de la vidéo.
- Vidéo sur le second écran : c'est le bon écran (Review Focus 3).
- Lecture dans la fenêtre d'aperçu ; « Afficher » ; « Supprimer » met la vidéo dans la Corbeille ; « Refaire ».

- [ ] **Step 5 : commit** — `Record videos on macOS, with an in-window player`

### Task 6: Réglages

**Files:**
- Create :
  - `src/ShowTime.Mac/Settings/SettingsWindowController.cs` (formulaire en `NSGridView`) ;
  - `src/ShowTime.Mac/Settings/HotkeyField.cs`.
- Modify : `StatusMenu.cs`, qui active « Réglages… ».

**Interfaces:**
- Consumes :
  - `SettingsForm.From(AppSettings)` et `SettingsEditor.TryApply(SettingsForm)` (exemple de dossier : `/Users/moi/Captures`) ;
  - `IStartupRegistration` ;
  - `IAudioDeviceLister` ;
  - `HotkeyNaming.MacOS`.
- Produces : `HotkeyField : NSTextField`. Il capture `KeyDown` avec ses modificateurs et affiche le raccourci (`Ctrl`, `Option`, `Maj`, `Cmd`).

- [ ] **Step 1 : implémenter**

- Mêmes champs que sous Windows.
- La case « Lancer ShowTime à l'ouverture de session » s'appuie sur `SMAppService`.
- Sous la case « Son de l'ordinateur », un texte d'aide : `platform.SystemAudioRequirement`.

- [ ] **Step 2 : vérifier** — `macos-app` est vert ; `SettingsEditorTests` passent.

- [ ] **Step 3 : checklist utilisateur, tâche 6**

- Saisie de raccourcis avec `Cmd` et `Option`.
- Un doublon est refusé.
- Un dossier relatif est refusé.
- Lancement à l'ouverture de session : actif après redémarrage, puis désactivé.
- Les réglages sont conservés après relance.

- [ ] **Step 4 : commit** — `Add the settings window on macOS`

### Task 7: Release v0.5.0

(v0.3.0 est sortie le 2026-10-22 pour Windows seul, avec les sous-titres : l'app Mac prend la version suivante.)

**Files:**
- Modify :
  - `.github/workflows/release.yml` : job `macos`, identique au build CI, qui produit `ShowTime-<version>-macos.zip` ; `publish` dépend de `windows` et de `macos` ;
  - `Directory.Build.props` : `<Version>0.5.0</Version>` ;
  - `README.md` : section macOS (installation, clic droit > Ouvrir, autorisations, `brew install ffmpeg`, BlackHole, `--diagnostics`) ;
  - `CLAUDE.md` : app Mac, `ShowTime.Mac.slnx`, version de Xcode ;
  - `docs/test-checklist.md` : checklist macOS complète, regroupant les checklists des tâches 1 à 6.

- [ ] **Step 1 : mettre à jour les fichiers**

- [ ] **Step 2 : vérifier** — CI verte sur les quatre jobs.

- [ ] **Step 3 : commit** — `Prepare ShowTime 0.5.0 for Windows and macOS`

- [ ] **Step 4 : remettre la main à l'utilisateur**

Il déroule la checklist macOS complète. La fusion et le tag `v0.5.0` se font seulement avec son accord explicite.

## Surlignage à la souris (ajouté le 2026-10-24)

Le cœur est prêt (`Highlighter`, `HighlightTrail`, `HighlightTrigger`, ports `IHighlightInput` et
`IHighlightOverlayFactory`) et `MacHotkeys.Register(hotkey, pressed, released)` signale déjà le relâchement. Il reste,
pour l'app macOS :
- `MacHighlightInput` : appui et relâchement par Carbon (sans permission Accessibilité), `IsHeld` tenu par ces deux
  événements, position par `CGEventGetLocation(CGEventCreate(0))` convertie en pixels physiques (facteur de l'écran).
  Bouton latéral : moniteur global `NSEvent` (`otherMouseDown/Up`), sans permission mais sans pouvoir avaler le clic ;
  l'avaler demanderait un `CGEventTap`, donc la permission Accessibilité.
- Overlay : `NSWindow` sans bordure, transparente, `ignoresMouseEvents`, au-dessus des fenêtres, `sharingType` par
  défaut pour qu'AVFoundation la capture (la barre, elle, est en `.none`).
- Vérifier si macOS garde `Ctrl+Espace` (« Sélectionner la source de saisie précédente ») quand il y a plusieurs
  sources de saisie ; si oui, passer le défaut macOS (`SettingsDefaults.ForMacOS`) à `Ctrl+Shift+Space`.
- Spec : `docs/superpowers/specs/2026-10-24-mouse-highlight-design.md`.
