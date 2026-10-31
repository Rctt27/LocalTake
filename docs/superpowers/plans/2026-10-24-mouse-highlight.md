# Surlignage à la souris pendant l'enregistrement — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** pendant un enregistrement, tant que l'utilisateur maintient un déclencheur (`Ctrl+Espace` par défaut, ou un bouton latéral de souris), une traînée jaune translucide suit la souris et apparaît dans la vidéo ; au relâchement, elle s'estompe en 500 ms.

**Architecture:** une fenêtre WinForms transparente, topmost et traversable par les clics (comme `RecordingFrame`), mais **non exclue de la capture** : gdigrab l'enregistre avec l'écran. Toute la logique (déclencheur, traînée, fondu, boucle d'images sur `TimeProvider`) est dans le cœur (`Highlighter`, `HighlightTrail`, `HighlightTrigger`) ; Windows fournit deux ports : l'entrée (`IHighlightInput` : raccourci ou hook souris, état tenu, position du curseur) et l'overlay (`IHighlightOverlay`).

**Tech Stack:** .NET 10, C#, WPF + WinForms, Win32 en `LibraryImport`, xunit v3 + Shouldly + NSubstitute, `FakeTimeProvider`.

**Spec:** `docs/superpowers/specs/2026-10-24-mouse-highlight-design.md`

## Global Constraints

- Rien ne quitte la machine : aucun appel réseau.
- Textes d'interface en français ; code et commentaires en anglais ; densité de commentaires du code voisin.
- Analyzers `latest-recommended`, warnings as errors ; `dotnet format ShowTime.slnx` avant chaque commit (la CI refuse le code non formaté).
- Couverture de lignes du cœur ≥ 90 % (`python build/check-coverage.py TestResults/core.cobertura.xml 90`).
- Coordonnées écran en **pixels physiques** partout (`PixelRect`, nouveau `PixelPoint`).
- `settings.json` est une interface publique : nouvelle clé `HighlightTrigger`, un ancien fichier doit continuer à se charger (défaut de la plateforme).
- Défaut `HighlightTrigger` : `"Ctrl+Space"` sur Windows et macOS.
- Rendu : jaune `#FFD400`, opacité de fenêtre 0,4, trait de 18 px à 100 % multiplié par l'échelle DPI, bouts et jointures arrondis, pas d'anticrénelage (fond en couleur clé magenta).
- Fondu : `0.4 × (1 − t)²`, t = temps depuis le relâchement / 500 ms, borné à [0, 1] ; fenêtre cachée à 0.
- Boucle d'images : 16 ms.
- Le déclencheur n'est enregistré que dans l'état `Recording` de `RecordingSession` (ni Ready, ni décompte, ni Finishing).
- Une nouvelle fonctionnalité commence par un test en échec.
- Ne jamais régénérer `tests/fixtures/legacy` ; un changement délibéré se fait dans le test, avec un commentaire.
- Travail sur une branche (pas sur `main`) ; chaque message de commit se termine par les lignes d'attribution de la session.
- Pas de changement de `<Version>` dans ce plan : la release (0.4.0, MINOR) est décidée à part par l'utilisateur.

## Review Focus

1. **Le déclencheur est encore tenu quand l'enregistrement s'arrête** (clic sur ■ ou Stop au clavier) : aucune traînée ne doit rester à l'écran, la boucle s'arrête. → test `Disabling_while_held_closes_the_overlay_and_stops_the_frames` (tâche 3).
2. **Ctrl relâché avant Espace** : la traînée s'estompe tout de suite. → test `Releasing_a_modifier_first_ends_the_hold` (tâche 6).
3. **Un `HighlightTrigger` invalide ou vide dans `settings.json`** (édité à la main) : l'enregistrement fonctionne, sans indication de surlignage sur la barre. → test `An_invalid_trigger_does_not_prevent_recording` (tâche 5).
4. **La souris sort de la zone enregistrée** (multi-écran) : les points hors zone sont dessinés hors de la fenêtre, donc rognés, sans exception. → test `Points_outside_the_area_are_accepted` (tâche 7).
5. **Un appui très long** (une minute de gestes) : mémoire et temps de dessin bornés. → test `Oldest_points_go_beyond_the_limit` (tâche 2).

---

### Task 1: `PixelPoint` et `HighlightTrigger`

**Files:**
- Create: `src/ShowTime.Core/Geometry/PixelPoint.cs`
- Create: `src/ShowTime.Core/Recording/HighlightTrigger.cs`
- Modify: `src/ShowTime.Core/Hotkeys/Hotkey.cs` (`TryParse`)
- Test: `tests/ShowTime.Core.Tests/Recording/HighlightTriggerTests.cs`, `tests/ShowTime.Core.Tests/Hotkeys/HotkeyTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct PixelPoint(int X, int Y);` (namespace `ShowTime.Core.Geometry`)
  - `Hotkey.TryParse(string? text, out Hotkey hotkey, bool allowPlainKey)` ; l'overload existant à deux paramètres appelle celui-ci avec `false`.
  - `public enum SideMouseButton { XButton1, XButton2 }`
  - `public readonly record struct HighlightTrigger` avec `Hotkey? Key`, `SideMouseButton? Button`, `static HighlightTrigger FromKey(Hotkey)`, `static HighlightTrigger FromButton(SideMouseButton)`, `static bool TryParse(string? text, out HighlightTrigger trigger)`, `string Format(HotkeyNaming naming)`, `ToString()` (forme du fichier), `bool BlocksTyping`.

- [ ] **Step 1: Écrire les tests en échec**

```csharp
[Fact] public void A_combination_is_a_key_trigger()
{
    HighlightTrigger.TryParse("Ctrl+Space", out var t).ShouldBeTrue();
    t.Key.ShouldBe(new Hotkey(HotkeyModifiers.Control, KeyCode.Space));
    t.Button.ShouldBeNull();
    t.BlocksTyping.ShouldBeFalse();
}

[Theory]
[InlineData("Space", true)]
[InlineData("A", true)]
[InlineData("F13", false)]
public void A_plain_key_is_allowed_and_says_whether_it_types(string text, bool blocksTyping)
{
    HighlightTrigger.TryParse(text, out var t).ShouldBeTrue();
    t.Key!.Value.Modifiers.ShouldBe(HotkeyModifiers.None);
    t.BlocksTyping.ShouldBe(blocksTyping);
}

[Theory]
[InlineData("XButton1", SideMouseButton.XButton1, "Souris 4")]
[InlineData("xbutton2", SideMouseButton.XButton2, "Souris 5")]
public void Side_buttons_are_mouse_triggers(string text, SideMouseButton button, string shown)
{
    HighlightTrigger.TryParse(text, out var t).ShouldBeTrue();
    t.Button.ShouldBe(button);
    t.Key.ShouldBeNull();
    t.Format(HotkeyNaming.Windows).ShouldBe(shown);
    t.ToString().ShouldBe(button.ToString());
}

[Theory]
[InlineData(null)] [InlineData("")] [InlineData("  ")] [InlineData("Ctrl+")] [InlineData("XButton3")] [InlineData("Foo")]
public void Anything_else_is_refused(string? text) => HighlightTrigger.TryParse(text, out _).ShouldBeFalse();

[Fact] public void Keys_are_shown_in_the_platform_naming()
{
    HighlightTrigger.TryParse("Ctrl+Alt+Space", out var t).ShouldBeTrue();
    t.Format(HotkeyNaming.MacOS).ShouldBe("Ctrl+Option+Space");
    t.ToString().ShouldBe("Ctrl+Alt+Space");
}
```

Dans `HotkeyTests` :

```csharp
[Fact] public void A_plain_key_is_only_parsed_when_allowed()
{
    Hotkey.TryParse("Space", out _).ShouldBeFalse();
    Hotkey.TryParse("Space", out var h, allowPlainKey: true).ShouldBeTrue();
    h.ShouldBe(new Hotkey(HotkeyModifiers.None, KeyCode.Space));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test --project tests/ShowTime.Core.Tests`
Expected: échec de compilation (`HighlightTrigger`, `PixelPoint`, overload de `TryParse` inconnus).

- [ ] **Step 3: Implémenter**

`Hotkey.TryParse(..., allowPlainKey)` : candidat accepté si `Key != KeyCode.None && (allowPlainKey || candidate.IsValid)`. `HighlightTrigger.TryParse` : `"XButton1"`/`"XButton2"` (insensible à la casse) → bouton, sinon `Hotkey.TryParse(text, out k, allowPlainKey: true)`. `BlocksTyping` : `Key is { Modifiers: HotkeyModifiers.None } k && !Hotkey.AllowedWithoutModifier(k.Key)`. Doc XML de `HighlightTrigger` : touche simple permise parce que le déclencheur n'est actif que pendant un enregistrement.

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test --project tests/ShowTime.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
dotnet format ShowTime.slnx
git add src/ShowTime.Core tests/ShowTime.Core.Tests
git commit -m "Parse the highlight trigger: a shortcut, a plain key or a side mouse button"
```

---

### Task 2: `HighlightTrail`

**Files:**
- Create: `src/ShowTime.Core/Recording/HighlightTrail.cs`
- Test: `tests/ShowTime.Core.Tests/Recording/HighlightTrailTests.cs`

**Interfaces:**
- Consumes: `PixelPoint` (tâche 1).
- Produces: `public sealed class HighlightTrail` avec `public const double FullOpacity = 0.4;`, `public const int MaxPoints = 2000;`, `public const int MinStep = 2;`, `public static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(500);`, `IReadOnlyList<PixelPoint> Points`, `void Begin(PixelPoint at)`, `bool Add(PixelPoint at)`, `static double FadeOpacity(TimeSpan sinceRelease)`.

- [ ] **Step 1: Écrire les tests en échec**

```csharp
[Fact] public void Begin_starts_a_new_trail_at_the_point()
{
    var trail = new HighlightTrail();
    trail.Begin(new(0, 0)); trail.Add(new(10, 0));
    trail.Begin(new(50, 50));
    trail.Points.ShouldBe([new PixelPoint(50, 50)]);
}

[Fact] public void Points_closer_than_the_minimum_step_are_skipped()
{
    var trail = new HighlightTrail();
    trail.Begin(new(0, 0));
    trail.Add(new(1, 1)).ShouldBeFalse();
    trail.Add(new(2, 0)).ShouldBeTrue();
    trail.Points.Count.ShouldBe(2);
}

[Fact] public void Oldest_points_go_beyond_the_limit()
{
    var trail = new HighlightTrail();
    trail.Begin(new(0, 0));
    for (var i = 1; i <= HighlightTrail.MaxPoints; i++)
        trail.Add(new(i * 2, 0));
    trail.Points.Count.ShouldBe(HighlightTrail.MaxPoints);
    trail.Points[0].ShouldBe(new PixelPoint(2, 0));
}

[Theory]
[InlineData(0, 0.4)]
[InlineData(250, 0.1)]
[InlineData(500, 0)]
[InlineData(2000, 0)]
public void The_fade_is_fast_then_gentle(int milliseconds, double opacity) =>
    HighlightTrail.FadeOpacity(TimeSpan.FromMilliseconds(milliseconds)).ShouldBe(opacity, 1e-9);

[Fact] public void The_fade_only_decreases()
{
    var values = Enumerable.Range(0, 33).Select(i => HighlightTrail.FadeOpacity(TimeSpan.FromMilliseconds(i * 16))).ToList();
    values.ShouldBe(values.OrderByDescending(v => v).ToList());
}
```

- [ ] **Step 2: Vérifier l'échec** — Run: `dotnet test --project tests/ShowTime.Core.Tests` ; Expected: échec de compilation.

- [ ] **Step 3: Implémenter `HighlightTrail`**

Distance euclidienne au dernier point, comparée au carré à `MinStep²`. `Add` sur une traînée vide ajoute le point. Au-delà de `MaxPoints`, retirer le plus ancien (une `List<PixelPoint>` suffit : `RemoveAt(0)` sur 2000 éléments à 60 Hz est négligeable).

- [ ] **Step 4: Vérifier le succès** — même commande ; Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "Add the highlight trail and its fade"` (après `dotnet format`).

---

### Task 3: ports et `Highlighter`

**Files:**
- Create: `src/ShowTime.Core/Ports/IHighlightPorts.cs`
- Create: `src/ShowTime.Core/Recording/Highlighter.cs`
- Modify: `tests/ShowTime.Core.Tests/Fakes.cs`
- Test: `tests/ShowTime.Core.Tests/Recording/HighlighterTests.cs`

**Interfaces:**
- Consumes: `HighlightTrigger`, `HighlightTrail`, `PixelPoint`, `PixelRect`, `IUiThread`, `AppSettings.HighlightTrigger` (ajoutée ici à `AppSettings` en propriété `public string HighlightTrigger { get; set; } = "Ctrl+Space";` ; la tâche 4 la branche sur `SettingsDefaults`).
- Produces:

```csharp
public interface IHighlightInput
{
    /// <summary>Starts watching the trigger. <paramref name="pressed"/> runs on the UI thread.</summary>
    /// <returns>False when the system refused it, usually because another application holds it.</returns>
    bool Register(HighlightTrigger trigger, Action pressed);
    void Unregister();
    /// <summary>Whether the registered trigger is still held down, modifiers included.</summary>
    bool IsHeld { get; }
    /// <summary>The mouse position, in physical screen pixels.</summary>
    PixelPoint Pointer { get; }
}

public interface IHighlightOverlay : IDisposable
{
    /// <summary>Shows the overlay if it is hidden and draws the trail, in screen pixels.</summary>
    void Draw(IReadOnlyList<PixelPoint> points);
    void SetOpacity(double opacity);
    void Hide();
}

public interface IHighlightOverlayFactory
{
    /// <summary>A hidden overlay over the recorded area, captured with it.</summary>
    IHighlightOverlay Create(PixelRect area);
}

public sealed class Highlighter(
    AppSettings settings, IHighlightInput input, IHighlightOverlayFactory overlays, IUiThread uiThread,
    TimeProvider clock, HotkeyNaming hotkeyNaming) : IDisposable
{
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);
    /// <returns>The trigger as the bar shows it, or null when it is empty, invalid or refused.</returns>
    public string? Enable(PixelRect area);
    public void Disable();
    public void Dispose(); // Disable()
}
```

Comportement de `Highlighter` :
- `Enable` : `Disable()` d'abord ; trigger illisible → null sans overlay ; `input.Register` refusé → null sans overlay ; sinon `overlays.Create(area)` et `trigger.Format(hotkeyNaming)`.
- Appui : ignoré sans overlay ou si déjà tenu (Carbon peut répéter) ; sinon tenu, `trail.Begin(input.Pointer)`, `SetOpacity(FullOpacity)`, `Draw(points)`, et timer `clock.CreateTimer(_ => uiThread.Post(Tick), null, FrameInterval, FrameInterval)` s'il n'existe pas.
- `Tick` tenu : `!input.IsHeld` → relâché, horodatage `clock.GetTimestamp()` ; sinon `trail.Add(input.Pointer)` et `Draw` si un point a été ajouté.
- `Tick` relâché : `FadeOpacity(clock.GetElapsedTime(releasedAt))` ; ≤ 0 → `Hide()` et timer libéré ; sinon `SetOpacity`.
- `Disable` : `input.Unregister()`, timer et overlay libérés, état remis à zéro.

Fakes à ajouter dans `Fakes.cs` : `FakeHighlightInput` (`Registered` : `HighlightTrigger?`, `Refused` : bool, `Unregistrations` : int, `IsHeld` et `Pointer` modifiables, `Press()` qui appelle l'action enregistrée) ; `FakeHighlightOverlay` (`Draws` : `List<IReadOnlyList<PixelPoint>>` copiés, `Opacity`, `Visible`, `Disposed`, `OpacityChanges` : int) ; `FakeHighlightOverlayFactory` (`Overlay`, `Area`).

- [ ] **Step 1: Écrire les tests en échec** (`Highlighter` construit avec `ImmediateUiThread` et `FakeTimeProvider`, zone `new PixelRect(0, 0, 800, 600)`)

```csharp
[Fact] public void Enabling_registers_the_trigger_and_returns_how_the_bar_shows_it()
{
    _highlighter.Enable(Area).ShouldBe("Ctrl+Space");
    _input.Registered.ShouldBe(HighlightTrigger.FromKey(new Hotkey(HotkeyModifiers.Control, KeyCode.Space)));
    _overlays.Area.ShouldBe(Area);
    _overlays.Overlay!.Visible.ShouldBeFalse();
}

[Theory] [InlineData("")] [InlineData("Foo")]
public void An_empty_or_invalid_trigger_enables_nothing(string text)
{
    _settings.HighlightTrigger = text;
    _highlighter.Enable(Area).ShouldBeNull();
    _overlays.Overlay.ShouldBeNull();
}

[Fact] public void A_refused_trigger_enables_nothing()
{
    _input.Refused = true;
    _highlighter.Enable(Area).ShouldBeNull();
    _overlays.Overlay.ShouldBeNull();
}

[Fact] public void Holding_draws_a_trail_that_follows_the_pointer()
{
    _highlighter.Enable(Area);
    _input.Pointer = new(100, 100); _input.IsHeld = true; _input.Press();
    Overlay.Visible.ShouldBeTrue(); Overlay.Opacity.ShouldBe(0.4);
    Overlay.Draws[^1].ShouldBe([new PixelPoint(100, 100)]);
    _input.Pointer = new(110, 100); _clock.Advance(Highlighter.FrameInterval);
    Overlay.Draws[^1].ShouldBe([new PixelPoint(100, 100), new PixelPoint(110, 100)]);
}

[Fact] public void Releasing_fades_the_trail_then_hides_it_and_stops_the_frames()
{
    HoldAt(new(100, 100));
    _input.IsHeld = false; _clock.Advance(Highlighter.FrameInterval);
    Advance(TimeSpan.FromMilliseconds(250));
    Overlay.Opacity.ShouldBeInRange(0.05, 0.15); Overlay.Visible.ShouldBeTrue();
    Advance(TimeSpan.FromMilliseconds(300));
    Overlay.Visible.ShouldBeFalse();
    var changes = Overlay.OpacityChanges;
    Advance(TimeSpan.FromSeconds(1));
    Overlay.OpacityChanges.ShouldBe(changes);
}

[Fact] public void Pressing_again_during_the_fade_starts_a_new_trail_at_full_opacity() { /* HoldAt(a); release; Advance(100ms); Pointer=b; IsHeld=true; Press() → Opacity 0.4, Draws[^1] == [b] */ }

[Fact] public void A_repeated_press_while_held_keeps_the_trail() { /* HoldAt(a); move to b; Advance(16ms); Press() → Draws[^1] still [a, b] */ }

[Fact] public void Disabling_while_held_closes_the_overlay_and_stops_the_frames()
{
    HoldAt(new(100, 100));
    var draws = Overlay.Draws.Count;
    _highlighter.Disable();
    Overlay.Disposed.ShouldBeTrue(); _input.Unregistrations.ShouldBe(1);
    _input.Pointer = new(200, 200); Advance(TimeSpan.FromSeconds(1));
    Overlay.Draws.Count.ShouldBe(draws);
}
```

`HoldAt(p)` : `Enable`, `Pointer = p`, `IsHeld = true`, `Press()`. `Advance(d)` : avance par pas de `FrameInterval`.

- [ ] **Step 2: Vérifier l'échec** — `dotnet test --project tests/ShowTime.Core.Tests` ; échec de compilation.
- [ ] **Step 3: Implémenter les ports et `Highlighter`** selon le comportement ci-dessus.
- [ ] **Step 4: Vérifier le succès** — même commande ; PASS.
- [ ] **Step 5: Commit** — `git commit -m "Draw a highlight trail while the trigger is held, then fade it out"`.

---

### Task 4: réglage `HighlightTrigger`

**Files:**
- Modify: `src/ShowTime.Core/Settings/SettingsDefaults.cs`, `AppSettings.cs`, `SettingsEditor.cs` (`SettingsForm`)
- Modify: `src/ShowTime.Windows/Settings/SettingsWindow.xaml.cs:246` (passer `_settings.HighlightTrigger` pour compiler ; le champ arrive en tâche 8)
- Test: `tests/ShowTime.Core.Tests/Settings/SettingsDefaultsTests.cs`, `SettingsEditorTests.cs`, `tests/ShowTime.Infrastructure.IntegrationTests/Settings/JsonSettingsStoreTests.cs`, `tests/ShowTime.Platform.Tests/MacOS/MacPlatformTests.cs`

**Interfaces:**
- Produces: paramètre positionnel `string HighlightTrigger` en dernier dans `SettingsDefaults` ; `AppSettings.HighlightTrigger` initialisée depuis les défauts (doc : « Held during a recording to highlight with the mouse: a shortcut, a plain key, or XButton1/XButton2. ») ; `SettingsForm.HighlightTrigger` juste après `HotkeyVideoStop`, incluse dans `Hotkeys` (contrôle des doublons), copiée et `Trim()` par `From`/`ApplyTo`.

- [ ] **Step 1: Écrire les tests en échec**
  - `SettingsDefaultsTests` : ajouter `d.HighlightTrigger` à `Shortcuts` ; nouveau `Both_platforms_highlight_with_Ctrl_Space` (`ForWindows("x").HighlightTrigger` et `ForMacOS("x").HighlightTrigger` valent `"Ctrl+Space"`).
  - `SettingsEditorTests` : `The_highlight_trigger_cannot_reuse_a_session_shortcut` (`Form() with { HighlightTrigger = "Ctrl+R" }` → `"Le raccourci Ctrl+R est attribué deux fois."`) ; `The_highlight_trigger_is_saved_trimmed` (`" XButton1 "` → `_settings.HighlightTrigger == "XButton1"`).
  - `JsonSettingsStoreTests` : liste des clés écrites `+ "HighlightTrigger"` avec le commentaire `// Deliberate change: the highlight trigger is a new key, which older files simply lack.` ; renommer le test en `..._the_transcription_and_the_highlight` ; dans le test de chargement de v0.1.0, `s.HighlightTrigger.ShouldBe("Ctrl+Space");`.
  - `MacPlatformTests.Every_default_macOS_shortcut_can_be_registered` : ajouter `d.HighlightTrigger`.
- [ ] **Step 2: Vérifier l'échec** — `dotnet test --solution ShowTime.slnx` ; échecs de compilation et d'assertion.
- [ ] **Step 3: Implémenter** (retirer l'initialiseur provisoire de la tâche 3 dans `AppSettings`).
- [ ] **Step 4: Vérifier le succès** — `dotnet test --solution ShowTime.slnx` ; PASS.
- [ ] **Step 5: Commit** — `git commit -m "Add the HighlightTrigger setting, Ctrl+Space by default"`.

---

### Task 5: `RecordingSession` active le surlignage pendant l'enregistrement, la barre l'indique

**Files:**
- Modify: `src/ShowTime.Core/Ports/IRecordingPorts.cs` (`IRecordingView`)
- Modify: `src/ShowTime.Core/Recording/RecordingSession.cs` (constructeur, `SetState`, `Dispose`)
- Modify: `src/ShowTime.Windows/Recording/WpfRecordingView.cs`, `RecordingBar.xaml`, `RecordingBar.xaml.cs`
- Modify: `tests/ShowTime.Core.Tests/Fakes.cs` (`FakeRecordingView`)
- Test: `tests/ShowTime.Core.Tests/Recording/RecordingSessionTests.cs`, `tests/ShowTime.Windows.Tests/Recording/RecordingBarTests.cs`

**Interfaces:**
- Consumes: `Highlighter.Enable(PixelRect) : string?`, `Highlighter.Disable()` (tâche 3).
- Produces:
  - `IRecordingView.ShowHighlightShortcut(string? shortcut)` : « During the recording: the trigger to hold, or null when there is none. »
  - `RecordingSession` : nouveau paramètre `Highlighter highlighter` juste après `IGlobalHotkeys sessionHotkeys`.
  - `RecordingBar.ShowHighlightShortcut(string? shortcut)`.

- [ ] **Step 1: Écrire les tests en échec**
  - `FakeRecordingView` : `public string? HighlightShortcut { get; private set; }` et `public int HighlightShown { get; private set; }` (hors de `Calls`, pour ne pas toucher aux séquences des tests existants).
  - Dans `RecordingSessionTests`, champs `_highlightInput = new FakeHighlightInput()`, `_overlays = new FakeHighlightOverlayFactory()`, et `new Highlighter(_settings, _highlightInput, _overlays, new ImmediateUiThread(), _clock, HotkeyNaming.Windows)` passé au constructeur.

```csharp
[Fact] public async Task Nothing_is_highlighted_while_the_bar_waits_for_Start()
{
    await ReadyAsync();
    _highlightInput.Registered.ShouldBeNull();
    View.HighlightShown.ShouldBe(0);
}

[Fact] public async Task Recording_enables_the_highlighting_over_the_area()
{
    await RecordingAsync();
    _highlightInput.Registered.ShouldNotBeNull();
    View.HighlightShortcut.ShouldBe("Ctrl+Space");
    _overlays.Area.ShouldBe(new PixelRect(10, 20, 640, 480));
}

[Fact] public async Task Stopping_ends_the_highlighting()
{
    await RecordingAsync();
    _ = _session.StopAsync();
    _highlightInput.Unregistrations.ShouldBe(1);
    _overlays.Overlay!.Disposed.ShouldBeTrue();
}

[Fact] public async Task Cancelling_ends_the_highlighting() { /* RecordingAsync; View.ClickCancel() → Unregistrations 1, overlay Disposed */ }

[Fact] public async Task Stop_during_the_countdown_never_enables_the_highlighting() { /* ReadyAsync; StartAsync sans l'attendre; StopAsync → Registered null */ }

[Fact] public async Task An_invalid_trigger_does_not_prevent_recording()
{
    _settings.HighlightTrigger = "Foo";
    await RecordingAsync();
    View.Calls.ShouldContain("recording");
    View.HighlightShown.ShouldBe(1);
    View.HighlightShortcut.ShouldBeNull();
}
```

  - `RecordingBarTests` : `The_highlight_shortcut_is_shown_while_recording` : `ShowReady`, `ShowRecording`, `ShowHighlightShortcut("Ctrl+Space")` → `HighlightHint.Text == "Surligner : maintenir Ctrl+Space"` et visible ; `ShowFinishing()` → masqué ; `ShowHighlightShortcut(null)` → masqué.
- [ ] **Step 2: Vérifier l'échec** — `dotnet test --solution ShowTime.slnx` ; échecs de compilation.
- [ ] **Step 3: Implémenter**
  - `SetState` : mémoriser l'état précédent ; entrée en `Recording` → `_view?.ShowHighlightShortcut(highlighter.Enable(_target))` ; sortie de `Recording` → `highlighter.Disable()`. `Dispose` appelle `highlighter.Dispose()`.
  - `RecordingBar.xaml` : `TextBlock x:Name="HighlightHint"`, `Foreground="#AAA"`, après `StatusHint`, `Visibility="Collapsed"`. `ShowFinishing` le masque.
  - `WpfRecordingView.ShowHighlightShortcut` relaie à la barre.
- [ ] **Step 4: Vérifier le succès** — `dotnet test --solution ShowTime.slnx` ; PASS.
- [ ] **Step 5: Commit** — `git commit -m "Enable the highlight only while recording and show its trigger on the bar"`.

---

### Task 6: `WindowsHighlightInput`

**Files:**
- Create: `src/ShowTime.Platform.Windows/WindowsHighlightInput.cs`
- Modify: `src/ShowTime.Platform.Windows/Win32.cs`
- Modify: `tests/ShowTime.Windows.Tests/ShowTime.Windows.Tests.csproj` (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` pour le `LibraryImport` de `SendInput` du test)
- Test: `tests/ShowTime.Windows.Tests/Recording/WindowsHighlightInputTests.cs` (sur le thread `Sta`, qui pompe les messages dont ont besoin `RegisterHotKey` et le hook)

**Interfaces:**
- Consumes: `IHighlightInput`, `HighlightTrigger`, `WindowsHotkeys`, `WindowsKeys.VirtualKey`.
- Produces: `public sealed unsafe class WindowsHighlightInput(IUiThread uiThread) : IHighlightInput, IDisposable`.
- Win32 à ajouter : `short GetAsyncKeyState(int vKey)`, `bool GetCursorPos(out POINT point)`, `nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nint, nint, nint> lpfn, nint hmod, uint dwThreadId)`, `bool UnhookWindowsHookEx(nint hhk)`, `nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam)`, `struct POINT { int X; int Y; }`, `struct MSLLHOOKSTRUCT { POINT pt; uint mouseData; uint flags; uint time; nuint dwExtraInfo; }`, constantes `WH_MOUSE_LL = 14`, `WM_XBUTTONDOWN = 0x020B`, `WM_XBUTTONUP = 0x020C`, `XBUTTON1 = 1`, `XBUTTON2 = 2`, `VK_SHIFT = 0x10`, `VK_CONTROL = 0x11`, `VK_MENU = 0x12`, `VK_LWIN = 0x5B`, `VK_RWIN = 0x5C`.

Comportement :
- Touche : un `WindowsHotkeys` propre, créé au premier `Register` (thread UI), enregistre le raccourci. `IsHeld` = bit `0x8000` de `GetAsyncKeyState` pour la touche **et** chaque modificateur du raccourci (Meta : `VK_LWIN` ou `VK_RWIN`).
- Bouton : `SetWindowsHookExW(WH_MOUSE_LL, &MouseProc, GetModuleHandleW(null), 0)` sur le thread UI. Une instance active à la fois (champ statique). `MouseProc` : `WM_XBUTTONDOWN/UP` dont `HIWORD(mouseData)` est le bouton voulu → appui (une seule fois tant que tenu, via `uiThread.Post`) ou relâchement, et retourne 1 pour l'avaler (sinon le navigateur ferait « Précédent ») ; tout le reste → `CallNextHookEx` immédiatement (le hook doit répondre vite).
- `Pointer` : `GetCursorPos` (pixels physiques, le processus est Per-Monitor-V2).
- `Unregister` : raccourcis libérés, hook retiré. `Dispose` : `Unregister` puis le `WindowsHotkeys`.

- [ ] **Step 1: Écrire les tests en échec** (sautés si `SendInput` renvoie 0, comme un bureau non interactif)

```csharp
[Fact] public async Task A_held_shortcut_is_reported_until_released()
{
    // Ctrl+Alt+Shift+F23 : sans effet ailleurs.
    var pressed = 0;
    await Sta.Run(() => _input.Register(Trigger("Ctrl+Alt+Shift+F23"), () => pressed++).ShouldBeTrue());
    KeysDown(VK_CONTROL, VK_MENU, VK_SHIFT, VK_F23);
    await Sta.Run(Sta.DoEvents);
    pressed.ShouldBe(1);
    _input.IsHeld.ShouldBeTrue();
    KeysUp(VK_F23);
    _input.IsHeld.ShouldBeFalse();
    KeysUp(VK_SHIFT, VK_MENU, VK_CONTROL);
}

[Fact] public async Task Releasing_a_modifier_first_ends_the_hold() { /* même raccourci ; KeysUp(VK_CONTROL) avant F23 → IsHeld false */ }

[Fact] public async Task A_side_button_is_reported_held_until_released() { /* Trigger("XButton2") ; SendInput XDOWN mouseData=XBUTTON2 → pressed 1, IsHeld true ; XUP → false */ }

[Fact] public async Task Unregistering_frees_the_shortcut() { /* Register puis Unregister ; un nouveau WindowsHotkeys enregistre le même raccourci */ }
```

`SendInput` est traité de façon asynchrone : après chaque envoi, `await Sta.Run(Sta.DoEvents)` avant de lire `IsHeld`. `finally` dans chaque test : relâcher toutes les touches et boutons envoyés, `Unregister`.

- [ ] **Step 2: Vérifier l'échec** — `dotnet test --project tests/ShowTime.Windows.Tests` ; échec de compilation.
- [ ] **Step 3: Implémenter** `Win32` et `WindowsHighlightInput`.
- [ ] **Step 4: Vérifier le succès** — même commande ; PASS (ou SKIP explicite sans bureau interactif).
- [ ] **Step 5: Commit** — `git commit -m "Watch the highlight trigger on Windows: shortcut or side mouse button"`.

---

### Task 7: `HighlightOverlay` (Windows), capturé dans la vidéo

**Files:**
- Create: `src/ShowTime.Windows/Recording/HighlightOverlay.cs` (avec `HighlightOverlayFactory`)
- Modify: `src/ShowTime.Windows/Interop/Native.cs` (`GetWindowDisplayAffinity`, si absent ; `GetWindowLong` pour le test)
- Test: `tests/ShowTime.Windows.Tests/Recording/HighlightOverlayTests.cs`

**Interfaces:**
- Consumes: `IHighlightOverlay`, `IHighlightOverlayFactory`, `PixelRect.ToRectangle()`.
- Produces: `internal sealed class HighlightOverlay : Form, IHighlightOverlay` (`public HighlightOverlay(Rectangle area)`, `internal static readonly Color Ink = Color.FromArgb(255, 212, 0)`, `internal const float Thickness = 18`) ; `internal sealed class HighlightOverlayFactory : IHighlightOverlayFactory`.

Comportement : reprend `RecordingFrame` (bordure, `ShowWithoutActivation`, `CreateParams` layered/transparent/toolwindow/noactivate, `TopMost`, `WM_DPICHANGED` ignoré, `BackColor = TransparencyKey = Magenta`) **sans** `ExcludeFromCapture`. `Opacity = 0.4` à la création, `DoubleBuffered = true`. Dessin : `SmoothingMode.None` (l'anticrénelage mélangerait le jaune au magenta et laisserait une frange rose), stylo `Ink` de largeur `Thickness * DeviceDpi / 96f`, `LineCap.Round`, `LineJoin.Round` ; un seul point → disque du même diamètre. `Draw` : points translatés de `-area.Location`, invalidation du rectangle des nouveaux points (depuis le `Draw` précédent) gonflé de la largeur du trait, ou de toute la fenêtre si la traînée a redémarré, puis `Show()` si cachée. `SetOpacity` : `Opacity = value` (pas de repeinte : le fondu est quasi gratuit). `IHighlightOverlay.Hide` en implémentation explicite : vide les points puis `Hide()` du Form. `Dispose` est celui du Form.

- [ ] **Step 1: Écrire les tests en échec**

```csharp
[Fact] public async Task The_overlay_is_click_through_never_activated_and_captured() => await Sta.Run(() =>
{
    using var overlay = new HighlightOverlay(new Rectangle(0, 0, 200, 100));
    overlay.Draw([new PixelPoint(10, 50), new PixelPoint(190, 50)]);
    overlay.Visible.ShouldBeTrue();
    overlay.Opacity.ShouldBe(0.4, 0.01);
    var style = Native.GetWindowLong(overlay.Handle, Native.GWL_EXSTYLE);
    (style & (Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED)).ShouldBe(Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED);
    Native.GetWindowDisplayAffinity(overlay.Handle, out var affinity).ShouldBeTrue();
    affinity.ShouldBe(0u); // WDA_NONE : contrairement au cadre rouge, il doit être dans la vidéo
});

[Fact] public async Task Opacity_changes_and_hiding_clears_the_trail() { /* SetOpacity(0.2) → Opacity 0.2 ; ((IHighlightOverlay)o).Hide() → Visible false */ }

[Fact] public async Task Points_outside_the_area_are_accepted() { /* Draw([(-500, -500), (5000, 5000)]) puis DoEvents : pas d'exception */ }

[Fact] public async Task The_trail_is_in_the_video()
{
    // Comme SyncTests : fenêtre noire topmost en (0, 0, 320, 240), overlay par-dessus sur la même zone,
    // Draw([(20, 120), (300, 120)]), 1,5 s enregistrées avec FfmpegVideoRecorderFactory(new DirectShowGdigrab(() => false),
    // new WasapiSystemAudio()) et new VideoOptions(30, null, false). Sauté sans ffmpeg.
    // Image à 1 s, pixel (160, 120) : ffmpeg -ss 1 -i f.mp4 -frames:v 1 -vf crop=1:1:160:120 -f rawvideo -pix_fmt rgb24 -
    // 40 % de #FFD400 sur noir ≈ (102, 85, 0) :
    line.R.ShouldBeInRange(70, 140); line.G.ShouldBeInRange(55, 120); line.B.ShouldBeLessThan(40);
    // Pixel (160, 40), hors du trait : reste noir.
    off.R.ShouldBeLessThan(30); off.G.ShouldBeLessThan(30); off.B.ShouldBeLessThan(30);
}
```

Le dernier test lit la sortie binaire d'ffmpeg (`StandardOutput.BaseStream`), pas du texte.
- [ ] **Step 2: Vérifier l'échec** — `dotnet test --project tests/ShowTime.Windows.Tests` ; échec de compilation.
- [ ] **Step 3: Implémenter `HighlightOverlay` et `HighlightOverlayFactory`.**
- [ ] **Step 4: Vérifier le succès** — même commande ; PASS.
- [ ] **Step 5: Commit** — `git commit -m "Draw the highlight trail in a click-through overlay the video captures"`.

---

### Task 8: branchement, fenêtre de réglages et documentation

**Files:**
- Modify: `src/ShowTime.Windows/App.xaml.cs` (`ConfigureServices`, vers la ligne 112)
- Modify: `src/ShowTime.Windows/Settings/SettingsWindow.xaml`, `SettingsWindow.xaml.cs`
- Modify: `README.md` (tableau « Raccourcis par défaut »), `docs/test-checklist.md`, `CLAUDE.md` (section Video)

**Interfaces:**
- Consumes: `WindowsHighlightInput` (tâche 6), `HighlightOverlayFactory` (tâche 7), `Highlighter` (tâche 3), `HighlightTrigger.BlocksTyping` (tâche 1).

- [ ] **Step 1: Composition** — dans `ConfigureServices` : `services.AddSingleton<IHighlightInput>(sp => new WindowsHighlightInput(sp.GetRequiredService<IUiThread>()));`, `services.AddSingleton<IHighlightOverlayFactory, HighlightOverlayFactory>();`, `services.AddSingleton(sp => ActivatorUtilities.CreateInstance<Highlighter>(sp, HotkeyNaming.Windows));`. `RecordingSession` le reçoit par `ActivatorUtilities`.

- [ ] **Step 2: Fenêtre de réglages**
  - Ligne 8 de la grille des raccourcis : libellé « Surligner (maintenir) », `TextBox x:Name="HighlightTriggerBox"` (style `HotkeyBox`, tooltip « Cliquez puis appuyez sur la touche ou la combinaison à maintenir, ou cliquez avec un bouton latéral de la souris. »).
  - Ligne 9 : `TextBlock x:Name="HighlightWarning"` (style `Hint`, `Collapsed`) : « Pendant l'enregistrement, cette touche n'écrira plus rien dans les applications. » Visible quand `HighlightTrigger.TryParse(text, out var t) && t.BlocksTyping` (sur `TextChanged`).
  - `PreviewKeyDown` propre à ce champ : comme `HotkeyBox_PreviewKeyDown` (Tab, Entrée, Échap gardent leur rôle ; Retour arrière et Suppr vident), mais accepte une touche seule : `new Hotkey(modifiers, keyCode)` dès que `keyCode != KeyCode.None`. Impr. écran au relâchement comme les autres champs.
  - `PreviewMouseDown` : `XButton1`/`XButton2` → `Text = "XButton1"`/`"XButton2"`, `e.Handled = true`.
  - `DefaultHotkeys_Click` remplit aussi ce champ ; `Save_Click` passe `HighlightTriggerBox.Text` (remplace le `_settings.HighlightTrigger` provisoire de la tâche 4).
  - Texte d'aide : « … Démarrer, Arrêter et Surligner ne sont actifs que pendant une vidéo. »

- [ ] **Step 3: Documentation**
  - README : ligne « Surligner avec la souris (maintenir, seulement pendant une vidéo) | `Ctrl+Espace` », et une phrase : bouton latéral de souris possible ; boutons des souris gaming à associer à F13–F24 dans le logiciel du fabricant.
  - `docs/test-checklist.md`, section vidéo : maintenir `Ctrl+Espace` en survolant un bouton (traînée jaune qui suit, rien n'est tapé) ; relâcher (fondu d'environ 0,5 s) ; Espace seul tape un espace dans la démo ; lâcher Ctrl avant Espace (fondu immédiat) ; bouton latéral dans un navigateur (pas de « Précédent ») ; traînée visible et fondu fluide dans le MP4 ; multi-écran et 150 % ; pas de surlignage pendant le décompte ni après Stop.
  - CLAUDE.md, section Video : un paragraphe sur `Highlighter` (déclencheur actif seulement en Recording, `IHighlightInput` : `RegisterHotKey` + `GetAsyncKeyState` ou hook `WH_MOUSE_LL`), sur l'overlay **volontairement non exclu** de la capture, à l'inverse de `RecordingFrame`, et sur le fondu par opacité de fenêtre.

- [ ] **Step 4: Vérification complète**

```
dotnet build ShowTime.slnx -c Release
dotnet test --solution ShowTime.slnx
dotnet test --project tests/ShowTime.Core.Tests --coverage --coverage-output-format cobertura --coverage-output core.cobertura.xml
python build/check-coverage.py TestResults/core.cobertura.xml 90
dotnet format ShowTime.slnx --verify-no-changes
```
Expected: build sans warning, tous les tests PASS (ou SKIP motivés), couverture ≥ 90 %, aucun changement de format.

- [ ] **Step 5: Essai réel** — tuer `ShowTime`, `dotnet run --project src/ShowTime.Windows`, enregistrer une zone, dérouler la section ajoutée à `docs/test-checklist.md`, puis `ffmpeg -i <video>.mp4 -vf fps=10 frames/%03d.png` pour voir la traînée et le fondu image par image.

- [ ] **Step 6: Commit** — `git commit -m "Wire the mouse highlight into the app and the settings window"`.

---

### Task 9: macOS — relâchement des raccourcis Carbon

**Files:**
- Modify: `src/ShowTime.Platform.MacOS/Native.cs` (`kEventHotKeyReleased = 6`, `uint GetEventKind(nint evt)`)
- Modify: `src/ShowTime.Platform.MacOS/MacHotkeys.cs`
- Modify: `docs/superpowers/plans/2026-10-17-macos-native.md` (note de suivi)
- Test: `tests/ShowTime.Platform.Tests/MacOS/MacPlatformTests.cs`

**Interfaces:**
- Produces: `public bool MacHotkeys.Register(Hotkey hotkey, Action pressed, Action released)` ; `IGlobalHotkeys.Register` appelle cet overload avec une action vide. Le handler s'installe pour `kEventHotKeyPressed` **et** `kEventHotKeyReleased` (deux `EventTypeSpec`) et choisit l'action selon `GetEventKind(evt)`.

- [ ] **Step 1: Test en échec** — `A_shortcut_can_be_registered_with_a_release_action` (macOS seulement, comme le test voisin) : `Register(Ctrl+Alt+Shift+F19, () => { }, () => { })` renvoie true, puis `UnregisterAll()`.
- [ ] **Step 2: Vérifier l'échec** — `dotnet test --project tests/ShowTime.Platform.Tests` ; échec de compilation (sur toute plateforme).
- [ ] **Step 3: Implémenter**, puis ajouter au plan macOS une section « Surlignage » : `MacHighlightInput` (Carbon appui/relâchement, position par `CGEventGetLocation(CGEventCreate(0))` convertie en pixels physiques, bouton latéral par moniteur global `NSEvent` sans permission mais sans pouvoir avaler le clic) ; overlay `NSWindow` transparente, `ignoresMouseEvents`, `sharingType` par défaut (capturée) ; vérifier si `Ctrl+Espace` (sources de saisie) est pris par macOS, sinon défaut macOS `Ctrl+Shift+Space`.
- [ ] **Step 4: Vérifier le succès** — même commande ; PASS (SKIP hors macOS).
- [ ] **Step 5: Commit** — `git commit -m "Report the release of Carbon shortcuts for the macOS highlight"`.
