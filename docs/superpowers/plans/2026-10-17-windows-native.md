# ShowTime Windows natif — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** faire de l'app WPF, branchée sur le cœur, l'app Windows définitive (v0.2.0), et retirer Avalonia.

**Architecture :** `ShowTime.Windows` (WPF, `net10.0-windows`) ne contient que des vues et des adaptateurs. Les appels Win32 passent par `ShowTime.Platform.Windows`, la validation des réglages par `SettingsEditor`, le flou par `PixelBuffer.Pixelate` et le diagnostic par `ShowTime.Infrastructure`.

**Tech stack :**
- .NET 10 (SDK épinglé par `global.json`), WPF et WinForms (seulement pour `NotifyIcon`) ;
- tests : xunit.v3 sur Microsoft.Testing.Platform, Shouldly, NSubstitute ;
- Central Package Management.

**Spec :** `C:\Users\FlowUP\.claude\plans\j-aimerais-profesionnaliser-la-structure-vivid-fog.md` (plan d'ensemble, section « Plans détaillés », point 1).

## Global Constraints

- **Branche :** `refactor/native-windows`, créée depuis `refactor/phase-3-avalonia`.
- **Aucun appel réseau, aucune télémétrie :** les captures ne quittent jamais la machine.
- **Langues :** chaînes visibles en français ; code, commentaires et messages de commit en anglais.
- **Compatibilité :** un `settings.json` de la v0.1.0 se charge sans perte. Les tests de `tests/fixtures/legacy` restent verts sans modification.
- **Interface publique inchangée :**
  - exe `ShowTime.exe` (`AssemblyName` `ShowTime`) ;
  - mutex `Local\ShowTime.SingleInstance` ;
  - dossier des réglages, raccourcis par défaut, noms de fichiers.
- **Build :** `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `dotnet format ShowTime.slnx --verify-no-changes` propre.
- **Couverture du cœur :** ≥ 90 %, vérifiée par `python3 build/check-coverage.py TestResults/core.cobertura.xml 90`.
- **Commits :** message en anglais, terminé par les lignes d'attribution de la session.

## Review Focus

1. **Lancement par double-clic avec `--diagnostics`** (raccourci Windows, sans console parente) : le rapport doit tout de même exister sur disque. Test dans la tâche 6.
2. **Couleurs entre `PixelBuffer` (BGRA) et `BitmapSource` :** une inversion rouge/bleu, ou un alpha prémultiplié par erreur, donnerait des captures aux couleurs fausses. Test dans la tâche 4.
3. **Export de l'éditeur avec un zoom différent de 100 % :** l'image exportée a la taille et les pixels de la capture. Test dans la tâche 3.
4. **Raccourci déclenché pendant qu'une fenêtre modale est ouverte :** l'action s'exécute sur le thread WPF, sans exception de thread. `WindowsHotkeys` reçoit `DispatcherUiThread`. Point de checklist manuelle dans la tâche 4.
5. **Suppression d'une vidéo encore ouverte dans le lecteur :** les deux `MediaElement` sont fermés avant l'appel à `IFileTrash`, et la vidéo part dans la Corbeille. Le comportement actuel de `VideoPreviewWindow` est conservé quand l'appel passe par le port. Point de checklist manuelle dans la tâche 4.

---

### Task 1: Le diagnostic dans Infrastructure

**Files:**
- Create: `src/ShowTime.Infrastructure/Diagnostics/DiagnosticsReport.cs`
- Modify: `src/ShowTime.App/Diagnostics.cs` (devient un appel à `DiagnosticsReport` ; supprimé à la tâche 2)
- Test: `tests/ShowTime.Infrastructure.IntegrationTests/Diagnostics/DiagnosticsReportTests.cs`

**Interfaces:**
- Produces :
  - `public sealed class DiagnosticsReport(IAppPaths paths, AppSettings settings, FfmpegLocator locator, IFfmpegPlatform platform, IAudioDeviceLister audio, IScreenGrabber grabber, IGlobalHotkeys hotkeys, ILogger<DiagnosticsReport> log)` ;
  - `public Task<int> RunAsync(TextWriter output)` : renvoie 0 si tout va bien, 2 sinon.
- Consumes : les ports de `ShowTime.Core.Ports` et de `ShowTime.Core.Recording`.

- [ ] **Step 1 : écrire les tests qui échouent**

Ils s'appuient sur des fakes NSubstitute, et `settings.FfmpegPath` pointe sur un faux exe dans `TempFolder` :
- `All_checks_pass_returns_zero` :
  - `RunAsync` renvoie `0` ;
  - la sortie contient `[ok]   ffmpeg`, `[ok]   global shortcut: PrintScreen accepted` (avec `HotkeyRegion = "PrintScreen"`) et `Everything ShowTime needs is available.`.
- `Refused_shortcut_is_reported` : avec `hotkeys.Register(...)` qui renvoie `false`, `RunAsync` renvoie `2` et la sortie contient `[FAIL] global shortcut` et `1 check(s) failed.`.
- `Missing_ffmpeg_skips_audio_checks` : sans ffmpeg, la sortie contient `[FAIL] ffmpeg` avec `platform.InstallHint`, et ne contient pas `audio inputs`.
- `Shortcuts_are_released_after_the_check` : `hotkeys.Received(1).UnregisterAll()`.

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test --project tests/ShowTime.Infrastructure.IntegrationTests -c Release --filter-class "*DiagnosticsReportTests"`
Attendu : la compilation échoue (`DiagnosticsReport` n'existe pas).

- [ ] **Step 3 : déplacer le corps de `src/ShowTime.App/Diagnostics.cs` dans `DiagnosticsReport.RunAsync`**

Mêmes vérifications, mêmes lignes et même ordre. Les services résolus depuis le conteneur deviennent des paramètres du constructeur, et le rapport est journalisé via `log`. `Diagnostics.RunAsync` dans l'app Avalonia se réduit à résoudre `DiagnosticsReport` et à l'appeler. `DiagnosticsReport` est enregistré dans `Composition.cs`.

- [ ] **Step 4 : vérifier le succès**

Run : `dotnet test --solution ShowTime.slnx -c Release`
Attendu : tous les tests passent.

- [ ] **Step 5 : commit** — `Move the diagnostics report into the infrastructure, for both apps`

### Task 2: Retirer Avalonia et l'aperçu par images ffmpeg

**Files:**
- Delete :
  - `src/ShowTime.App/`, `tests/ShowTime.App.Tests/` ;
  - `src/ShowTime.Core/Ports/IVideoFrames.cs`, `src/ShowTime.Core/Recording/VideoInfo.cs`, `src/ShowTime.Infrastructure/Recording/FfmpegVideoFrames.cs` ;
  - `tests/ShowTime.Core.Tests/Recording/VideoInfoTests.cs`, `tests/ShowTime.Infrastructure.IntegrationTests/Recording/FfmpegVideoFramesTests.cs` ;
  - `build/package-macos.sh`, `build/macos/Info.plist`.
- Modify :
  - `ShowTime.slnx` ;
  - `Directory.Packages.props` : retirer `Avalonia*`, `Avalonia.Headless*`, `CommunityToolkit.Mvvm` et Skia s'ils ne sont plus référencés ;
  - `.github/workflows/ci.yml` : retirer l'étape « UI tests (headless) » et le job `package-macos` ; `package-windows` publie `src/ShowTime.Wpf` ;
  - `.github/workflows/release.yml` : retirer le job `macos` ; `publish` ne dépend plus que de `windows` ; le job `windows` publie `src/ShowTime.Wpf`.

- [ ] **Step 1 : supprimer, puis mettre à jour solution, paquets et workflows**

Avant de supprimer `VideoInfo` et `IVideoFrames`, vérifier qu'il ne reste aucune référence :

`rg -n "IVideoFrames|VideoInfo|FfmpegVideoFrames" src tests` → aucun résultat hors des fichiers supprimés.

- [ ] **Step 2 : vérifier**

Run : `dotnet format ShowTime.slnx --verify-no-changes; dotnet build ShowTime.slnx -c Release; dotnet test --solution ShowTime.slnx -c Release`
Attendu : 0 avertissement, tous les tests passent. Le nombre de tests baisse de ceux d'`App.Tests`, de `VideoInfoTests` et de `FfmpegVideoFramesTests`.

- [ ] **Step 3 : vérifier les commandes de la CI pour le portable**

Run : `dotnet test --project tests/ShowTime.Core.Tests -c Release --coverage --coverage-output-format cobertura --coverage-output core.cobertura.xml; python3 build/check-coverage.py tests/ShowTime.Core.Tests/bin/Release/net10.0/TestResults/core.cobertura.xml 90`
Attendu : seuil respecté. Adapter le chemin du rapport à celui que produit MTP.

- [ ] **Step 4 : commit** — `Drop the Avalonia app: Windows stays on WPF, macOS gets its own app`

### Task 3: `ShowTime.Windows`, et des tests WPF hors écran

**Files:**
- Rename :
  - `src/ShowTime.Wpf/` → `src/ShowTime.Windows/` ;
  - `ShowTime.Wpf.csproj` → `ShowTime.Windows.csproj`.

  Avec `git mv`. `AssemblyName` et `RootNamespace` restent `ShowTime`.
- Modify :
  - `ShowTime.Windows.csproj` : retirer le commentaire « Transitional… » ; ajouter `<InternalsVisibleTo Include="ShowTime.Windows.Tests" />` ;
  - `ShowTime.slnx`, `ci.yml`, `release.yml` (chemins) ;
  - `src/ShowTime.Windows/Editor/EditorWindow.xaml.cs` : `RenderImage()` et `SetZoom(double)` passent de `private` à `internal`.
- Create :
  - `tests/ShowTime.Windows.Tests/ShowTime.Windows.Tests.csproj` (`net10.0-windows`, `UseWPF`, référence `src/ShowTime.Windows`) ;
  - `tests/ShowTime.Windows.Tests/Sta.cs` ;
  - `tests/ShowTime.Windows.Tests/Editor/EditorExportTests.cs`.

**Interfaces:**
- Produces :
  - `internal static class Sta { public static Task Run(Action action); public static Task<T> Run<T>(Func<T> action); }` : exécute sur un thread STA dédié, doté d'un `Dispatcher` WPF, et propage les exceptions ;
  - `internal static class TestImages { public static BitmapSource Gradient(int width, int height); public static byte[] Pixels(BitmapSource image); }` : un dégradé opaque où chaque pixel est différent, et ses octets BGRA.

- [ ] **Step 1 : écrire le test qui fige l'export**

`Export_keeps_the_capture_size_and_pixels_at_any_zoom` :
- construire `new EditorWindow(TestImages.Gradient(301, 203), context)`, avec un `EditorContext` de fakes, puis `Show()` et `SetZoom(2.0)` (passé en `internal`) ;
- `RenderImage()` a `PixelWidth == 301`, `PixelHeight == 203`, et `TestImages.Pixels(...)` est égal à celui de l'image source.

- [ ] **Step 2 : lancer — attendu PASS**

C'est un test de caractérisation : il doit passer sur le code actuel.

Run : `dotnet test --project tests/ShowTime.Windows.Tests -c Release`

- [ ] **Step 3 : mettre à jour la doc et les commandes**

Dans `CLAUDE.md` et `README.md`, remplacer partout `src/ShowTime.App` et `ShowTime.Wpf` par `src/ShowTime.Windows`, et retirer les mentions d'Avalonia.

- [ ] **Step 4 : vérifier** — format, build et `dotnet test --solution ShowTime.slnx -c Release` sont verts.

- [ ] **Step 5 : commit** — `Make the WPF app the Windows app, with off-screen tests`

### Task 4: WPF sur `ShowTime.Platform.Windows`

**Files:**
- Modify :
  - `src/ShowTime.Windows/ShowTime.Windows.csproj` : référencer `ShowTime.Platform.Windows` ;
  - `Adapters/WpfImage.cs`, `Adapters/WpfScreen.cs`, `Capture/SelectionOverlay.cs`, `Recording/RecordingBar.xaml.cs`, `Recording/RecordingFrame.cs`, `Recording/VideoPreviewWindow.xaml.cs`, `Editor/EditorWindow.xaml.cs`, `Settings/SettingsWindow.xaml.cs`, `Tray/TrayController.cs`, `Tray/TrayIcon.cs`, `App.xaml.cs`.
- Delete :
  - `src/ShowTime.Windows/Capture/ScreenCapture.cs` ;
  - `src/ShowTime.Windows/Hotkeys/Hotkeys.cs` (`WpfKeyMap`, `HotkeyManager`) ;
  - de `Interop/Native.cs`, tout ce que `Platform.Windows/Win32.cs` fournit déjà. Il n'y reste que ce qui est propre aux fenêtres WPF (`WM_DPICHANGED`, `SetForegroundWindow`, styles `WS_EX_*` du cadre) ; si la liste est vide, supprimer le fichier.
- Test : `tests/ShowTime.Windows.Tests/Adapters/WpfImageTests.cs`

**Interfaces:**
- Consumes (`ShowTime.Platform.Windows`) :
  - `WindowsScreenGrabber : IScreenGrabber` ;
  - `WindowsNativeWindows : INativeWindows` ;
  - `WindowsHotkeys(IUiThread) : IGlobalHotkeys` ;
  - `WindowsShell : IShell` ;
  - `WindowsFileTrash : IFileTrash` ;
  - `WindowsStartupRegistration(WindowsStartupRegistration.RunKeyPath, "ShowTime", Environment.ProcessPath!) : IStartupRegistration`.
- Produces :
  - `WpfImage.FromPixels(PixelBuffer pixels) : WpfImage` ;
  - `WpfImage.ToPixels(BitmapSource source) : PixelBuffer` (BGRA non prémultiplié, comme `PixelBuffer`).

- [ ] **Step 1 : écrire les tests qui échouent**

- `Pixels_round_trip_without_changing_colours` : un `PixelBuffer` 3×2 de couleurs distinctes, opaques (`B=10,G=20,R=200,A=255`, …), passé par `ToPixels(FromPixels(p).Source)`, redonne exactement les mêmes octets.
- `Red_stays_red` : un pixel `B=0,G=0,R=255,A=255` donne une `BitmapSource` dont `CopyPixels` en `Bgra32` lit `[0,0,255,255]`.
- `Translucent_pixels_are_not_premultiplied` : `A=128,R=200` redonne `R=200` après l'aller-retour.

- [ ] **Step 2 : vérifier l'échec** — `dotnet test --project tests/ShowTime.Windows.Tests -c Release` ne compile pas (`FromPixels` absent).

- [ ] **Step 3 : implémenter `FromPixels` et `ToPixels`, puis passer chaque appel natif par le port**

`FromPixels` passe par `BitmapSource.Create(..., PixelFormats.Bgra32, ...)` puis `Freeze()`. `ToPixels` passe par `FormatConvertedBitmap` vers `Bgra32`.

| Aujourd'hui | Devient |
|---|---|
| `ScreenCapture.Capture` / `VisibleWindows` / `ToBitmapSource` | `IScreenGrabber` + `WpfImage.FromPixels` |
| `ScreenCapture.Normalize` (images ouvertes ou collées) | `WpfImage.FromPixels(WpfImage.ToPixels(image))` |
| `HotkeyManager` (y compris celui créé à la main pour `RecordingSession` dans `App.xaml.cs`) | `IGlobalHotkeys` résolu depuis le conteneur |
| `SetWindowDisplayAffinity` | `INativeWindows.ExcludeFromCapture` |
| `SetWindowPos` HWND_TOPMOST | `INativeWindows.RaiseAboveEverything` |
| `Process.Start("explorer.exe", …)` | `IShell.Reveal` / `OpenFolder` / `OpenFile` |
| `FileSystem.DeleteFile(… SendToRecycleBin)` | `IFileTrash.MoveToTrash`, appelé au même endroit, après la fermeture des lecteurs |
| `Registry` Run dans `SettingsWindow` | `IStartupRegistration` |

Les fenêtres qui en ont besoin reçoivent les ports par leur constructeur, depuis le conteneur.

- [ ] **Step 4 : vérifier**

Run : `dotnet build ShowTime.slnx -c Release; dotnet test --solution ShowTime.slnx -c Release; rg -n "DllImport|LibraryImport" src/ShowTime.Windows`
Attendu : tout est vert, et les seuls imports natifs restants sont ceux listés dans « Delete ».

- [ ] **Step 5 : essai réel** — lancer `dotnet run --project src/ShowTime.Windows`, puis :
  - `Impr. écran` et sélection d'une zone ;
  - `Ctrl+Impr. écran` ;
  - un raccourci pendant que les Réglages sont ouverts (Review Focus 4) ;
  - une courte vidéo, son aperçu, puis « Supprimer » : le fichier est dans la Corbeille (Review Focus 5).

- [ ] **Step 6 : commit** — `Call Win32 through the Windows platform adapters only`

### Task 5: Réglages sur `SettingsEditor`, flou sur `PixelBuffer.Pixelate`

**Files:**
- Modify :
  - `src/ShowTime.Windows/Settings/SettingsWindow.xaml.cs` : la validation des lignes ~220-295 est remplacée par `SettingsForm` + `SettingsEditor.TryApply` ;
  - `src/ShowTime.Windows/Editor/EditorWindow.xaml.cs` : `CreatePixelated` ;
  - `src/ShowTime.Windows/App.xaml.cs`.
- Test : `tests/ShowTime.Windows.Tests/Editor/EditorExportTests.cs`

**Interfaces:**
- Consumes :
  - `SettingsForm.From(AppSettings)` ;
  - `SettingsEditor(AppSettings, ISettingsStore, IFileSystem, string folderExample).TryApply(SettingsForm) : string?` (null = enregistré, sinon message en français) ;
  - `PixelBuffer.Pixelate(PixelRect, int columns, int rows)` ;
  - `EditorGeometry.PixelationGrid(PixelRect, int block)` et `EditorGeometry.PixelationBlock(PixelRect, double dpiScale)`.
- Produces : `internal void AddBlur(PixelRect area)` dans `EditorWindow`, appelé par l'outil Flou au relâchement de la souris.

- [ ] **Step 1 : écrire le test qui échoue**

`Blur_matches_the_core_pixelation` :
- sur `Gradient(200, 120)`, `AddBlur(new PixelRect(10, 20, 90, 50))` ;
- dans `RenderImage()`, la zone recadrée est égale octet pour octet à `WpfImage.ToPixels(source).Pixelate(area, columns, rows)`, avec `(columns, rows) = EditorGeometry.PixelationGrid(area, EditorGeometry.PixelationBlock(area, 1.0))` ;
- en dehors de la zone, les pixels ne changent pas.

- [ ] **Step 2 : vérifier l'échec**

Run : `dotnet test --project tests/ShowTime.Windows.Tests -c Release`
Attendu : la compilation échoue (`AddBlur` absent).

- [ ] **Step 3 : implémenter**

- `CreatePixelated` affiche `WpfImage.FromPixels(pixels.Pixelate(...)).Source`, à la taille de la zone, au lieu du `TransformedBitmap` (dont le filtrage ne donne pas des moyennes exactes).
- `SettingsWindow` construit un `SettingsForm` depuis ses champs et affiche le message renvoyé par `TryApply` dans le même `MessageBox` d'avertissement qu'aujourd'hui.
- L'exemple de dossier passé à `SettingsEditor` est `C:\Users\Moi\Captures`.
- Les messages doublons avec `SettingsEditor` sont supprimés de la fenêtre.

- [ ] **Step 4 : vérifier**

Run : `dotnet test --solution ShowTime.slnx -c Release`
Attendu : tout est vert, y compris les `SettingsEditorTests` du cœur, qui couvrent désormais la validation réelle de la fenêtre.

- [ ] **Step 5 : commit** — `Validate settings and blur with the core, as the Mac app will`

### Task 6: `ShowTime.exe --diagnostics`

**Files:**
- Modify : `src/ShowTime.Windows/App.xaml.cs` (avant le mutex : si `e.Args` contient `--diagnostics`, lancer le diagnostic puis `Shutdown(code)`).
- Create : `src/ShowTime.Windows/DiagnosticsConsole.cs`.
- Test : `tests/ShowTime.Windows.Tests/DiagnosticsConsoleTests.cs`.

**Interfaces:**
- Consumes : `DiagnosticsReport.RunAsync(TextWriter)` (tâche 1).
- Produces : `internal static class DiagnosticsConsole { public static TextWriter Open(string reportFile); }`.
  - Elle s'attache à la console parente (`AttachConsole(ATTACH_PARENT_PROCESS)`, ajouté dans `Platform.Windows/Win32.cs`).
  - Elle renvoie un `TextWriter` qui écrit à la fois sur cette console, si elle existe, et dans `reportFile`.
  - `reportFile` = `Path.Combine(paths.LogsFolder, "diagnostics.txt")`.

- [ ] **Step 1 : écrire le test qui échoue**

`Report_is_written_to_the_file_without_a_console` : dans un test sans console attachée, écrire `"[ok]   x"` via `DiagnosticsConsole.Open(temp)`, puis `Dispose()` → le fichier contient `[ok]   x`.

- [ ] **Step 2 : vérifier l'échec** (compilation).

- [ ] **Step 3 : implémenter**

`App.OnStartup` enregistre `DiagnosticsReport` dans le conteneur et appelle `RunAsync`. En fin de rapport, il ajoute la ligne `Report saved to <chemin>`.

- [ ] **Step 4 : vérifier**

Run : `dotnet build src/ShowTime.Windows -c Release; src\ShowTime.Windows\bin\Release\net10.0-windows\ShowTime.exe --diagnostics`
Attendu (PowerShell) : les lignes `[ok]` s'affichent dans la console, le code de sortie est `0` et `diagnostics.txt` existe dans le dossier du journal. Lancé par double-clic sur un raccourci avec l'argument, le fichier est aussi créé (Review Focus 1).

- [ ] **Step 5 : commit** — `Add --diagnostics to the Windows app`

### Task 7: Préparer la v0.2.0

**Files:**
- Modify :
  - `Directory.Build.props` : `<Version>0.2.0</Version>` ;
  - `README.md` : partie Windows seulement ; macOS marqué « en préparation » ; `--diagnostics` ;
  - `CLAUDE.md` : architecture finale (deux têtes), commandes ;
  - `docs/test-checklist.md` : section Windows sur `src/ShowTime.Windows`, avec l'aperçu vidéo intégré (lecture, miniatures au survol). La section macOS est remplacée par « voir le plan macOS ».

- [ ] **Step 1 : mettre à jour les quatre fichiers**

- [ ] **Step 2 : vérifier**

Run : `dotnet format ShowTime.slnx --verify-no-changes; dotnet build ShowTime.slnx -c Release; dotnet test --solution ShowTime.slnx -c Release; dotnet publish src/ShowTime.Windows -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist`
Attendu : tout est vert ; `dist\ShowTime.exe` démarre et `dist\ShowTime.exe --diagnostics` renvoie `0`.

- [ ] **Step 3 : commit** — `Prepare ShowTime 0.2.0 for Windows`

- [ ] **Step 4 : remettre la main à l'utilisateur**

Il déroule la checklist Windows. Push, PR, fusion et tag `v0.2.0` se font **seulement avec son accord explicite** (dépôt public).
