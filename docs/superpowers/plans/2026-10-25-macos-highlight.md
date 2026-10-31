# Surlignage à la souris sur Mac — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** sur Mac, pendant l'enregistrement, maintenir `Option+Espace` dessine la même traînée jaune que sous Windows (25 %, 14,4 points, pause de 0,3 s puis fondu de 0,5 s), visible dans la vidéo ; au clavier seulement.

**Architecture:** le cœur (`Highlighter`, `HighlightTrail`, `HighlightTrigger`) est déjà partagé ; l'app Mac remplace ses bouchons (`NoHighlightInput`, `NoHighlightOverlayFactory`) par deux adaptateurs : `MacHighlightInput` (raccourci Carbon avec relâchement, état des modificateurs, position de la souris) et `MacHighlightOverlay` (une `NSWindow` transparente que les clics traversent, au-dessus des fenêtres, **capturée** par ffmpeg). La conversion d'une position de souris Cocoa en pixels ShowTime va dans `MacScreenSpace` (testée sur tous les runners).

**Tech Stack:** .NET 10, C#, AppKit (liaisons .NET pour macOS), Carbon (`MacHotkeys`), xunit v3 + Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-24-macos-parity-design.md` (objectif 2). Plan 3 sur 4.

## Global Constraints

- Branche `feature/macos-app` ; commits terminés par les lignes d'attribution de la session.
- Tests en local : `tests/ShowTime.Core.Tests` et `tests/ShowTime.Infrastructure.IntegrationTests` seulement. `ShowTime.Platform.Tests` et `ShowTime.Windows.Tests` passent par la CI (`gh workflow run CI --ref feature/macos-app`, `gh run watch <id> --exit-status`) : ils affichent des fenêtres ou captent du son, ce qui fait planter une autre application sur le PC de l'utilisateur.
- L'app Mac ne se compile que sur la CI (job `macos-app`) et n'a pas de tests automatisés : ce qui se teste va dans le cœur ou `ShowTime.Platform.MacOS` ; le reste se vérifie à la main (`docs/test-checklist.md`).
- Aucune autorisation Accessibilité ni Surveillance de l'entrée : pas d'event tap ; boutons latéraux de souris non pris en charge sur Mac (le champ des réglages n'accepte que le clavier).
- Défaut macOS : `Alt+Space` dans `settings.json` (affiché « Option+Space ») ; Windows garde `Ctrl+Space`. S'il est refusé par macOS sur le runner (tâche 1), le défaut macOS devient `Ctrl+Shift+Space` (décision de l'utilisateur).
- Rendu identique à Windows : jaune `#FFD400`, opacité de fenêtre `HighlightTrail.FullOpacity` (0,25), trait de 14,4 points (Windows : 14,4 px à 100 %, multiplié par l'échelle), bouts et jointures arrondis, fondu par l'opacité de la fenêtre.
- Le déclencheur n'est enregistré que pendant l'état Recording (déjà assuré par `RecordingSession` et `Highlighter`).
- Textes d'interface en français ; code et commentaires en anglais ; `dotnet format` avant chaque commit.

## Review Focus

1. **Option relâchée avant Espace** : la traînée s'estompe tout de suite, comme sous Windows. → `IsHeld` exige la touche et ses modificateurs (tâche 3), vérifié à la main.
2. **Souris sur un autre écran que la zone** (deux écrans d'échelles différentes) : la position est convertie avec l'écran qui contient la souris, la traînée hors zone est rognée sans erreur. → `A_point_on_the_second_screen_uses_its_scale` et `A_point_off_every_screen_uses_the_nearest` (tâche 2).
3. **Un `settings.json` Mac avec `XButton1`** (copié d'un PC) : pas de surlignage, pas d'indication sur la barre, l'enregistrement fonctionne. → `MacHighlightInput.Register` refuse les boutons (tâche 3).
4. **macOS refuse `Option+Espace`** : la barre n'affiche rien, et le défaut a été changé si le runner le refuse. → `The_default_highlight_shortcut_can_be_registered` (tâche 1).
5. **La traînée apparaît dans la vidéo, pas le cadre ni la barre** : l'overlay n'est pas exclu de la capture. → étape manuelle de la checklist (tâche 5).

---

### Task 1: défaut macOS `Option+Espace`, vérifié sur le runner

**Files:**
- Modify: `src/ShowTime.Core/Settings/SettingsDefaults.cs` (`ForMacOS`)
- Test: `tests/ShowTime.Core.Tests/Settings/SettingsDefaultsTests.cs`, `tests/ShowTime.Platform.Tests/MacOS/MacPlatformTests.cs`

**Interfaces:**
- Produces: `SettingsDefaults.ForMacOS(...).HighlightTrigger == "Alt+Space"` (ou `"Ctrl+Shift+Space"` si l'étape 4 le décide).

- [ ] **Step 1: Tests en échec**
  - `SettingsDefaultsTests` : remplacer `Both_platforms_highlight_with_Ctrl_Space` par `Each_platform_has_its_highlight_shortcut` — Windows `"Ctrl+Space"`, macOS `"Alt+Space"`, et `Hotkey.TryParse("Alt+Space", …)` formaté en `HotkeyNaming.MacOS` donne `"Option+Space"` (commentaire `// Deliberate change: Ctrl+Space selects the input source on a Mac.`).
  - `MacPlatformTests` (macOS seulement, comme `A_shortcut_can_be_registered_and_released`) : `The_default_highlight_shortcut_can_be_registered` — `MacKeys.IsSystemShortcut` faux pour `Alt+Space`, et `new MacHotkeys(...).Register(hotkey, () => { }, () => { })` vrai, puis `UnregisterAll()`.
- [ ] **Step 2: Vérifier l'échec** — `dotnet test --project tests/ShowTime.Core.Tests` (local). Expected: `Each_platform_has_its_highlight_shortcut` échoue sur macOS.
- [ ] **Step 3: Implémenter** — `ForMacOS` : `HighlightTrigger: "Alt+Space"`, doc XML : Ctrl+Espace change la source de saisie sur Mac.
- [ ] **Step 4: Vérifier** — local (cœur) PASS, puis CI : `portable (macos-latest)` exécute `The_default_highlight_shortcut_can_be_registered`.
  - S'il échoue (macOS refuse les raccourcis à Option seule) : passer le défaut à `"Ctrl+Shift+Space"` (tests ajustés), ajouter au cœur `Hotkey.RejectedByMacOS` (`Modifiers` vaut `Alt` ou `Alt | Shift`) avec ses tests, et faire refuser ces combinaisons par le champ des réglages Mac (tâche 4) avec le message « macOS refuse les raccourcis faits seulement d'Option (et de Maj) : ajoutez Ctrl ou Cmd. ». Consigner la décision au registre.
- [ ] **Step 5: Commit** — `git commit -m "Highlight with Option+Space on a Mac, where Ctrl+Space switches the input source"`

---

### Task 2: position de la souris en pixels ShowTime (`MacScreenSpace`)

**Files:**
- Modify: `src/ShowTime.Platform.MacOS/MacScreenSpace.cs`
- Test: `tests/ShowTime.Platform.Tests/MacOS/MacScreenSpaceTests.cs` (code managé : exécuté sur tous les runners)

**Interfaces:**
- Produces: `public static PixelPoint MacScreenSpace.PointToPixel(IReadOnlyList<ScreenFrame> screens, IReadOnlyList<PixelRect> layout, double pointX, double pointY)` — l'écran dont le cadre Cocoa contient le point, sinon le plus proche ; puis `ToPixel`.

- [ ] **Step 1: Tests en échec** (deux écrans : principal 1440 × 900 points à l'échelle 2, second 1920 × 1080 points à l'échelle 1 à sa droite, disposés par `Layout`)
  - `A_point_on_the_main_screen_is_scaled_from_the_top_left` : le point Cocoa `(100, 800)` donne le pixel `(200, 200)`.
  - `A_point_on_the_second_screen_uses_its_scale` : `(1540, 980)` donne `ToPixel(second, layout[1], 1540, 980)`.
  - `A_point_off_every_screen_uses_the_nearest` : `(-50, 450)` (à gauche du principal) donne `ToPixel(principal, layout[0], -50, 450)`.
- [ ] **Step 2: Vérifier l'échec** — `dotnet build tests/ShowTime.Platform.Tests` (échec de compilation) ; les tests tournent ensuite par la CI.
- [ ] **Step 3: Implémenter** — distance du point au rectangle de chaque écran (0 s'il est dedans), le minimum l'emporte.
- [ ] **Step 4: Vérifier** — CI : jobs `windows`, `portable` verts (les trois tests passent partout).
- [ ] **Step 5: Commit** — `git commit -m "Convert a mouse position in Cocoa points to ShowTime's pixels"`

---

### Task 3: `MacHighlightInput` et `MacHighlightOverlay` (app Mac)

**Files:**
- Create: `src/ShowTime.Mac/Platform/MacHighlightInput.cs`, `src/ShowTime.Mac/Recording/MacHighlightOverlay.cs`
- Delete: `src/ShowTime.Mac/Recording/NoHighlight.cs`

**Interfaces:**
- Consumes: `IHighlightInput`, `IHighlightOverlay`, `IHighlightOverlayFactory` (cœur) ; `MacHotkeys.Register(hotkey, pressed, released)` ; `MacScreenSpace.PointToPixel` (tâche 2) ; `ScreenInfo` ; `HighlightTrail.FullOpacity`.
- Produces: `internal sealed class MacHighlightInput(IUiThread uiThread) : IHighlightInput, IDisposable` ; `internal sealed class MacHighlightOverlay : IHighlightOverlay` et `internal sealed class MacHighlightOverlayFactory(INativeWindows windows) : IHighlightOverlayFactory`.

- [ ] **Step 1: `MacHighlightInput`**
  - `Register` : un bouton (`trigger.Button`) → `false` (pas d'interception sans Accessibilité) ; une touche → un `MacHotkeys` propre (créé au premier appel) ; `pressed` met l'état tenu à vrai puis appelle l'action ; `released` remet l'état à faux. Les deux arrivent sur le fil principal (`MacHotkeys` passe par `IUiThread`).
  - `IsHeld` : état tenu **et** chaque modificateur du raccourci encore enfoncé selon `NSEvent.CurrentModifierFlags` (Option relâchée avant Espace termine la traînée, comme `GetAsyncKeyState` sous Windows).
  - `Cursor` : `NSEvent.CurrentMouseLocation` (points Cocoa) → `MacScreenSpace.PointToPixel` avec les cadres et la disposition de `ScreenInfo.Current()`.
  - `Unregister` / `Dispose` : `UnregisterAll`, état remis à faux.
- [ ] **Step 2: `MacHighlightOverlay`**
  - Fenêtre sans bordure sur la zone (`ScreenInfo.Holding(area).ToCocoa(area)`), `IsOpaque = false`, fond transparent, sans ombre, `IgnoresMouseEvents = true`, `ReleaseWhenClosed(false)`, au-dessus de tout (`INativeWindows.RaiseAboveEverything`) et **pas** `ExcludeFromCapture` (commentaire : à l'inverse du cadre rouge, la vidéo doit la montrer).
  - Vue de dessin : les points (pixels ShowTime) convertis en points de la vue — `x = (p.X − area.X) / scale`, `y = (area.Bottom − p.Y) / scale` (origine Cocoa en bas à gauche) ; `NSBezierPath` jaune `#FFD400`, largeur 14,4, bouts et jointures arrondis ; un seul point → disque de même diamètre.
  - `Draw` : copie des points, `NeedsDisplay = true`, `OrderFrontRegardless` si cachée ; `SetOpacity` : `AlphaValue` ; `Hide` : points vidés, `OrderOut` ; `Dispose` : `Close`.
- [ ] **Step 3: Vérifier** — CI : `macos-app` vert (compilation, trim sans avertissement, format).
- [ ] **Step 4: Commit** — `git commit -m "Draw the highlight trail on a Mac, from Carbon's press and release"`

---

### Task 4: branchement, barre et réglages Mac

**Files:**
- Modify: `src/ShowTime.Mac/Composition.cs`, `src/ShowTime.Mac/Recording/MacRecordingView.cs`, `src/ShowTime.Mac/Recording/RecordingBar.cs`, `src/ShowTime.Mac/Settings/SettingsWindowController.cs`, `src/ShowTime.Mac/Settings/HotkeyField.cs`

**Interfaces:**
- Consumes: `MacHighlightInput`, `MacHighlightOverlayFactory` (tâche 3) ; `HighlightTrigger.BlocksTyping` (cœur).
- Produces: `RecordingBar.ShowHighlightShortcut(string? shortcut)` ; `HotkeyField.AllowPlainKey` (bool).

- [ ] **Step 1: Composition** — `IHighlightInput` → `MacHighlightInput`, `IHighlightOverlayFactory` → `MacHighlightOverlayFactory` (à la place des bouchons) ; le reste (`Highlighter` avec `HotkeyNaming.MacOS`) est déjà en place.
- [ ] **Step 2: Barre** — un libellé gris « Surligner : maintenir Option+Space » (texte `$"Surligner : maintenir {shortcut}"`), caché par défaut, affiché par `ShowHighlightShortcut(shortcut)` quand `shortcut` n'est pas null, caché par `ShowReady` et `ShowFinishing` ; `MacRecordingView.ShowHighlightShortcut` le relaie (fin du bouchon).
- [ ] **Step 3: Réglages** — ligne « Surligner (maintenir) » dans la section des raccourcis : un `HotkeyField` avec `AllowPlainKey = true` (une touche seule est acceptée : le déclencheur n'est actif que pendant l'enregistrement), sous lui un texte d'aide caché sauf si `HighlightTrigger.TryParse(…).BlocksTyping` : « Pendant l'enregistrement, cette touche n'écrira plus rien dans les applications. » ; « Raccourcis par défaut » le remet ; `Save` passe sa valeur à `SettingsForm` (au lieu de `_context.Settings.HighlightTrigger`).
- [ ] **Step 4: Vérifier** — CI : `macos-app` vert.
- [ ] **Step 5: Commit** — `git commit -m "Wire the Mac highlight into the bar and the settings"`

---

### Task 5: documentation et checklist

**Files:**
- Modify: `README.md`, `docs/test-checklist.md` (partie macOS), `CLAUDE.md` (Highlight, Shortcuts)

- [ ] **Step 1: README** — colonne macOS du surlignage : `Option+Espace` (au lieu de « pas encore »), au clavier seulement sur Mac.
- [ ] **Step 2: Checklist macOS**, étape vidéo :
  - la barre montre « Surligner : maintenir Option+Space » pendant l'enregistrement, pas avant ni après ;
  - maintenir `Option+Espace` en survolant un bouton : traînée jaune translucide sous la souris, rien n'est tapé ; au relâchement, elle reste 0,3 s puis s'estompe ;
  - relâcher Option avant Espace : fondu immédiat ;
  - dans le MP4 : la traînée et son fondu sont visibles, ni le cadre ni la barre ;
  - deux écrans d'échelles différentes : la traînée est sous la souris sur l'écran enregistré ;
  - réglages : changer le raccourci, essayer `Espace` seul (avertissement), revenir au défaut.
- [ ] **Step 3: CLAUDE.md** — Highlight : sur macOS, `MacHighlightInput` (Carbon pressed/released + modificateurs de `NSEvent`, sans autorisation, pas de boutons de souris) et `MacHighlightOverlay` (`NSWindow` capturée) ; défaut macOS `Option+Space` (Ctrl+Space change la source de saisie). Shortcuts : le défaut de surlignage par plateforme.
- [ ] **Step 4: Commit** — `git commit -m "Document the highlight on a Mac"`
