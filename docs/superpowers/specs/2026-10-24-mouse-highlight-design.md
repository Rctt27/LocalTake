# Surlignage à la souris pendant l'enregistrement — étude de faisabilité et plan

## Contexte

Pendant un enregistrement, la personne qui présente (ex. un PM qui fait la démo d'une feature) veut attirer l'œil
sur un élément de l'écran pendant qu'elle en parle. Tant qu'elle **maintient un déclencheur**, une **traînée jaune
translucide** suit la souris ; au relâchement, elle **s'estompe progressivement**. La traînée doit apparaître
**dans la vidéo**, sur Windows comme sur le futur build macOS, sans enfreindre la règle « rien ne quitte la machine ».

Contrainte d'ergonomie : une main tient la souris, l'autre seule est libre. Le déclencheur doit se tenir sans
effort, sans regarder le clavier, et sans parasiter ni le propos ni le geste de la souris. Il est paramétrable,
et peut être un bouton latéral de souris.

## Verdict : faisable sur les deux plateformes

Principe : **une fenêtre transparente, au premier plan et traversable par les clics, dessine la traînée à l'écran ;
comme elle n'est PAS exclue de la capture, ffmpeg l'enregistre telle quelle.** C'est l'inverse de `RecordingFrame`
(`src/ShowTime.Windows/Recording/RecordingFrame.cs`), qui appelle `ExcludeFromCapture` justement pour ne pas
apparaître dans la vidéo. Ce que voit le présentateur est ce que verra le spectateur. La commande ffmpeg ne change
pas, rien n'est réencodé, et `SyncTests` n'est pas touché.

| Besoin | Windows | macOS |
|---|---|---|
| Dessin | Form WinForms `WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE`, topmost, sur la zone | `NSWindow` sans bordure, transparente, `ignoresMouseEvents`, au-dessus des fenêtres |
| Visible dans la vidéo | Oui : gdigrab copie avec `CAPTUREBLT`, qui inclut les fenêtres layered (cf. `WindowsScreenGrabber.cs:31`) | Oui : AVFoundation capture l'écran composé, sauf les fenêtres en `sharingType = .none` (réservé à la barre) |
| Position de la souris | `GetCursorPos` sondé à chaque image (pixels physiques) | `NSEvent.mouseLocation`, sans permission |
| Touche clavier : appui | `RegisterHotKey` (`WindowsHotkeys`) | Carbon `RegisterEventHotKey` (`MacHotkeys`) |
| Touche clavier : relâchement | `GetAsyncKeyState(vk)` sondé tant que la traînée est active | `kEventHotKeyReleased` de Carbon, sans permission Accessibilité |
| Bouton latéral de souris (X1/X2) | Hook `WH_MOUSE_LL`, posé seulement pendant l'enregistrement, qui avale le clic (sinon le navigateur ferait « Précédent ») ; pas de permission | `NSEvent` global monitor (`otherMouseDown/Up`) : sans permission, mais **ne peut pas avaler** le clic ; l'avaler exige un `CGEventTap`, donc la permission Accessibilité |
| Boutons « gaming » supplémentaires | Le logiciel du fabricant (G Hub, Synapse…) les mappe sur une touche : la mapper sur **F13–F24**, déjà acceptées seules par `Hotkey` et sans effet dans les applications | Pareil |

## Ergonomie du déclencheur

### La barre d'espace seule : possible, mais pas par défaut
Faisable : le raccourci n'est enregistré que pendant l'état Recording, comme Start/Stop
(`RecordingSession.RegisterSessionHotkeys`, `RecordingSession.cs:274`). `RegisterHotKey` et Carbon acceptent Espace
sans modificateur. C'est la touche la plus grosse, sous le pouce, trouvable sans regarder : le meilleur choix
ergonomique au clavier.

Contrepartie : pendant l'enregistrement, **Espace ne tape plus d'espace dans aucune application** (le raccourci global
l'avale). Si la démo comprend de la saisie (remplir un formulaire, taper une recherche), les espaces disparaissent.
Les autres usages d'Espace sont évités du même coup : défilement de page, clic sur le bouton qui a le focus.
- Hors enregistrement (sélection, Ready, décompte, finalisation), Espace reste normal.
- C'est pourquoi le défaut est `Ctrl+Espace` (ci-dessous) ; Espace seul reste une option du réglage.

Écarté : « appui court = espace tapé, appui long = surlignage ». Il faudrait un hook clavier bas niveau qui réinjecte
l'espace (`SendInput`), et sur macOS un `CGEventTap` avec la permission Accessibilité. C'est fragile (le texte
arrive en retard, les répétitions sont mal gérées) pour un gain faible.

### Règle des raccourcis
`Hotkey` n'accepte une touche sans modificateur que pour Impr. écran et F1–F24, car une touche simple enregistrée
globalement casserait toutes les applications. Exception pour le seul déclencheur de surlignage : il n'est actif que
pendant l'enregistrement, donc Espace (et toute touche simple) y est permis. La fenêtre de réglages le signale par un
avertissement si la touche choisie sert à écrire.

### Défaut retenu : `Ctrl+Espace`
Choisi par l'utilisateur : Espace reste libre pour taper pendant une démo de formulaire ou de traitement de texte.
Une seule main suffit (auriculaire sur Ctrl gauche, pouce sur Espace), sans regarder.
- Respecte la règle actuelle de `Hotkey` (il y a un modificateur) : aucune exception nécessaire pour le défaut.
- Ce que le raccourci capte, **seulement pendant l'enregistrement** : l'autocomplétion des IDE (VS Code, Rider),
  « sélectionner la colonne » d'Excel, et sur un Windows chinois ou japonais la bascule d'IME. Si le système détient
  déjà le raccourci, `RegisterHotKey` échoue et la barre ne l'affiche pas, comme pour Start/Stop aujourd'hui.
- Fin de la traînée : dès que **Espace ou Ctrl** est relâché (les deux sont sondés). Si Ctrl est lâché alors
  qu'Espace reste enfoncé, la répétition automatique d'Espace ne correspond plus au raccourci et taperait des espaces
  dans l'application : cas limite à noter dans la checklist, sans traitement spécial.
- Maintenir Ctrl en survol est sans effet ; seuls Ctrl+molette (zoom) et Ctrl+clic (nouvel onglet) changent pendant
  l'appui, ce qui n'est pas le geste visé.
- **macOS** : par défaut, `Ctrl+Espace` est aussi « Sélectionner la source de saisie précédente » (Réglages > Clavier >
  Raccourcis > Sources de saisie), actif dès qu'il y a plusieurs sources. À vérifier sur Mac : qui l'emporte entre
  Carbon et le raccourci système ? Si c'est le système, le défaut macOS devient `Ctrl+Shift+Espace`.

### Autres déclencheurs possibles (réglage)
- `Espace` seul : permis pour ce seul déclencheur (exception à la règle de `Hotkey`, voir plus haut), avec
  l'avertissement « Espace ne tapera plus d'espace pendant l'enregistrement ».
- Toute combinaison clavier, `F13`–`F24`, `XButton1` / `XButton2` (« Souris 4 / 5 » dans l'UI). La zone de saisie des
  réglages capte un clic latéral pour enregistrer un bouton de souris.
- Refus si le déclencheur est le même que Start ou Stop (`SettingsEditor.TryApply`).

## Rendu : surligneur avec fondu progressif
- Trait jaune uniforme (ex. `#FFD400`), épaisseur ~18 px multipliée par l'échelle DPI, bouts et jointures arrondis.
  Fond en couleur clé (schéma de `RecordingFrame`), opacité globale de fenêtre ~40 % : les chevauchements ne
  foncent pas, c'est l'effet surligneur.
- **Fondu au relâchement** : l'opacité de la fenêtre descend de 40 % à 0 en ~500 ms, courbe ease-out (rapide au
  début puis doux), une étape par image (~60 Hz). La vidéo à 30 i/s en garde ~15 images : un fondu fluide. Le trait
  reste immobile pendant le fondu ; la fenêtre se cache à 0.
- Nouvel appui pendant le fondu : le fondu est annulé, l'ancienne traînée effacée, une nouvelle commence à pleine
  opacité.
- Durée, couleur et opacité sont des constantes d'un `HighlightStyle` du cœur (pas de réglage utilisateur au départ).
- Coût : fenêtre affichée seulement pendant l'appui et le fondu, invalidation limitée au rectangle de la traînée. À
  mesurer en 4K pendant une capture.

## Conception

### Cœur (`src/ShowTime.Core`, testable, couverture 90 %)
- `Recording/HighlightTrigger` : valeur parsée depuis le réglage, soit une `Hotkey`, soit un `MouseButton`
  (`XButton1`, `XButton2`). `TryParse` / `Format` comme `Hotkey`, noms français dans l'UI.
- `Recording/HighlightTrail` : modèle pur sur `TimeProvider`. `Begin(point)`, `Add(point)`, `Release()`,
  `Snapshot(now)` → points + opacité. Règles : décimation (< 2 px), plafond de points, fondu ease-out après
  `Release()`, annulation du fondu par un nouveau `Begin`, `IsVisible`.
- Ports : `IHighlightOverlay` (`Show(PixelRect area)`, `Render(TrailSnapshot)`, `Hide()`) ; `IHighlightInput`
  (`Start(HighlightTrigger, Action pressed, Action released)`, `Stop()`, `PixelPoint Pointer`). C'est chaque
  plateforme qui transforme ses événements en appui et relâchement.
- `RecordingSession` : démarre `IHighlightInput` à l'entrée en Recording et l'arrête à Stop, Cancel et Finishing.
  Une boucle d'images sur le `TimeProvider` tourne tant que la traînée est tenue ou en fondu. Fenêtre fermée dans
  `CloseUi`.
- Réglages : `AppSettings.HighlightTrigger` (+ `SettingsDefaults` Windows/macOS, `SettingsEditor`, `SettingsForm`).
  Clé absente d'un ancien `settings.json` → défaut de la plateforme (comportement de `JsonSettingsStore`), donc
  compatible. Nouvelle fonctionnalité → version MINOR (0.4.0).

### Windows
- `Platform.Windows/WindowsHighlightInput` : touche → `RegisterHotKey` sur la fenêtre message-only existante, puis
  sondage `GetAsyncKeyState` pour le relâchement. Bouton → `SetWindowsHookEx(WH_MOUSE_LL)` sur le thread UI :
  `WM_XBUTTONDOWN/UP` du bon bouton avalés, tout le reste passe tout de suite (le hook doit répondre vite).
- `ShowTime.Windows/Recording/HighlightOverlay` (WinForms, à côté de `RecordingFrame`) : bornes = zone enregistrée
  en pixels physiques, `WM_DPICHANGED` ignoré, **sans** `ExcludeFromCapture`, `Opacity` animée pour le fondu.
- `RecordingBar` : affiche « Maintenir Ctrl+Espace pour surligner » (format du déclencheur).
- Réglages WPF : champ « Surligner (maintenir) », capture des boutons latéraux et avertissement si la touche sert à écrire.

### macOS (dans le plan `docs/superpowers/plans/2026-10-17-macos-native.md`)
- `MacHotkeys` : ajouter `kEventHotKeyReleased` au handler Carbon (et `MacKeys` connaît déjà Espace, `0x31`).
- Bouton latéral : global monitor sans permission, clic non avalé (documenté). L'avaler par `CGEventTap` reste une
  option future soumise à la permission Accessibilité.

## Étapes
0. `git fetch`, comparer `main` à `origin/main`, tirer si besoin ; `dotnet --list-sdks` contre `global.json`.
1. **Prototype jetable (Windows)** pour lever les trois inconnues avant le TDD : (a) `GetAsyncKeyState` de Ctrl et
   d'Espace donne bien l'état physique alors que `Ctrl+Espace` est pris par `RegisterHotKey` ; (b) gdigrab capture bien la fenêtre layered
   translucide et son fondu ; (c) le hook `WH_MOUSE_LL` avale X1/X2 sans ralentir la souris pendant une capture.
2. Cœur en TDD : `HighlightTrigger`, `HighlightTrail`, réglage + `SettingsEditor`, intégration dans `RecordingSession`.
3. Adaptateurs Windows, overlay, barre, fenêtre de réglages.
4. `MacHotkeys` : relâchement Carbon (testé dans `Platform.Tests` sur macOS) ; vérifier `Ctrl+Espace` face au
   raccourci système des sources de saisie et basculer le défaut macOS sur `Ctrl+Shift+Espace` si besoin.
5. Docs : README (raccourcis), `docs/test-checklist.md`, CLAUDE.md (section Video : overlay non exclu, Espace pendant
   l'enregistrement).

## Tests
- Core : parse/format de `HighlightTrigger` (Espace, F13, XButton1, combinaison, invalides) ; `HighlightTrail`
  (décimation, plafond, courbe du fondu, annulation par un nouvel appui) avec `FakeTimeProvider` ; `RecordingSession`
  (entrée démarrée seulement en Recording, arrêtée à Stop/Cancel, Espace libre pendant le décompte) ; `SettingsEditor`
  (refus si identique à Start/Stop, Espace accepté ici seulement).
- Windows.Tests : overlay non exclu de la capture, jamais activé, clics traversants ; opacité qui décroît.
- Intégration (Windows) : enregistrer une zone avec la traînée sur un fond connu ; une image extraite contient du
  jaune à l'endroit attendu, et une image ~250 ms après le relâchement un jaune atténué.
- Manuel (`docs/test-checklist.md`) : `Ctrl+Espace` pendant l'enregistrement (surligne, rien n'est tapé), Espace seul
  tape bien un espace dans un formulaire de la démo, lâcher Ctrl avant Espace (fondu immédiat) ; bouton latéral dans un navigateur (pas de « Précédent ») ; bouton gaming mappé sur F13 ;
  multi-écran, 150 % DPI ; fondu fluide dans le MP4.

## Vérification de bout en bout
1. `dotnet test --solution ShowTime.slnx` et seuil de couverture du cœur.
2. `dotnet run --project src/ShowTime.Windows`, enregistrer une zone, maintenir `Ctrl+Espace` en survolant un bouton,
   relâcher, puis refaire avec un bouton latéral.
3. `ffmpeg -i file.mp4 -vf "fps=10" frames/%03d.png` et vérifier la traînée puis le fondu sur les images.
