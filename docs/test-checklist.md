# Tests manuels

Les tests automatisés couvrent la logique, ffmpeg, la conversion des images et l'export de l'éditeur. Ce qui touche à l'écran réel, aux raccourcis système et aux autorisations se vérifie à la main, avec cette liste, avant une release.

Notez pour chaque ligne : ✅, ❌ (avec ce qui s'est passé) ou — (non testé). Joignez à vos retours le rapport de `LocalTake.exe --diagnostics` (aussi dans `diagnostics.txt`, dossier du journal) et le journal (voir README, « Fichiers »).

## Windows (`src/LocalTake.Windows`)

Avec l'exe publié (`dotnet publish src/LocalTake.Windows -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist`) ou l'artefact `LocalTake-windows` de la CI.

### Démarrage

- [ ] LocalTake fermé, `LocalTake.exe --diagnostics` dans un terminal : toutes les lignes en `[ok]`, code de sortie 0. (Si LocalTake tourne, ses raccourcis sont pris et « global shortcut » est en `[FAIL]` : c'est attendu.)
- [ ] L'icône apparaît dans la zone de notification ; un clic (gauche ou droit) ouvre le menu.
- [ ] Un second lancement affiche « LocalTake est déjà lancé » et ne crée pas de seconde icône.
- [ ] Avec TinyTake lancé, un avertissement liste les raccourcis refusés.
- [ ] Un `settings.json` de la v0.1.0 est repris tel quel (raccourcis, dossier, format, compteur).

### Captures

- [ ] `Impr. écran` : l'écran se fige et s'assombrit, la zone glissée reste claire avec sa taille affichée ; `Échap` annule.
- [ ] Un simple clic prend la fenêtre sous le curseur ; `Espace` bascule en mode fenêtre.
- [ ] Le curseur ne clignote pas quand l'écran se fige.
- [ ] Les couleurs de la capture sont exactes (pas de rouge et de bleu inversés).
- [ ] Sur deux écrans (si possible avec deux échelles différentes) : la sélection fonctionne sur chacun, et la capture a la taille en pixels réelle.
- [ ] `Alt+Impr. écran`, `Ctrl+Impr. écran` et « Capturer la dernière zone » fonctionnent.
- [ ] Un raccourci de capture pressé pendant qu'un éditeur est ouvert fonctionne.
- [ ] Depuis le menu, le menu lui-même n'apparaît pas dans la capture.
- [ ] Les quatre réglages « Après une capture » : éditeur, fichier (bulle de notification, un clic montre le fichier dans l'Explorateur), presse-papier, les deux.

### Éditeur

- [ ] Chaque outil, sa touche, `Maj` pour contraindre ; texte avec `Entrée` et `Maj+Entrée`.
- [ ] Déplacer et supprimer une annotation avec l'outil Sélection.
- [ ] Recadrer, puis `Ctrl+Z` : l'image entière revient avec ses annotations.
- [ ] Flou : la zone est pixelisée en blocs nets, à tous les zooms, et l'export aussi.
- [ ] `Ctrl+S` (PNG puis JPEG), `Ctrl+Maj+S`, `Ctrl+C` puis coller dans Paint : l'image a la taille de la capture, quel que soit le zoom.
- [ ] Après un enregistrement, le lien « Enregistré : … » ouvre l'Explorateur sur le fichier.
- [ ] Fermer sans enregistrer pose la question ; « Annuler » garde l'éditeur ouvert.
- [ ] « Ouvrir une image… » et « Coller depuis le presse-papier » (avec une image et sans).

### Vidéo

- [ ] `Ctrl+Maj+Impr. écran` : zone, cadre rouge autour, barre dessous ; `Ctrl+R` démarre après 3 secondes.
- [ ] Micro et son du PC (pilote TinyTake) : `ffmpeg -i video.mp4` montre une piste audio, et la fin de la phrase prononcée avant « Arrêter » est présente.
- [ ] Le cadre et la barre n'apparaissent pas dans la vidéo.
- [ ] Aperçu : la vidéo se lit dans la fenêtre ; `Espace`, `←` / `→` ; image au survol de la barre de défilement.
- [ ] « Afficher dans l'Explorateur » ouvre l'Explorateur sur la vidéo.
- [ ] « Supprimer » pendant la lecture : la vidéo part dans la Corbeille (et peut en être restaurée).
- [ ] « Refaire » : la vidéo part dans la Corbeille et un nouvel enregistrement commence.
- [ ] « Annuler » pendant l'enregistrement : aucun fichier ne reste.

### Réglages

- [ ] Saisie des raccourcis (dont `Impr. écran`), « Raccourcis de TinyTake », doublon refusé avec un message.
- [ ] Dossier relatif refusé ; « Parcourir… » pour le dossier et ffmpeg ; un chemin ffmpeg inexistant est refusé.
- [ ] « Lancer LocalTake au démarrage de Windows » : présent après redémarrage, puis désactivé.
- [ ] « Quitter » pendant un enregistrement pose la question ; avec un éditeur non enregistré aussi.

## macOS

Avec `LocalTake-x.y.z-macos.zip` d'une release, ou l'artefact `LocalTake-macos` de la CI (onglet *Actions* du dépôt, dernier passage de la branche, section *Artifacts*). Les étapes suivent l'ordre de construction de l'app (`docs/superpowers/plans/2026-10-17-macos-native.md`) ; ensemble, elles forment la checklist macOS complète d'une release.

Pour l'installer : décompresser `LocalTake-macos.zip`, glisser `LocalTake.app` dans Applications, puis clic droit > Ouvrir > Ouvrir (l'app n'est pas signée par un développeur identifié ; macOS ne le demande qu'une fois). Si macOS refuse sans proposer « Ouvrir » : Réglages Système > Confidentialité et sécurité > « Ouvrir quand même ».

### Étape 1 : l'app démarre

- [ ] Version de macOS (menu Pomme > À propos de ce Mac) : 14 (Sonoma) ou plus récente. Notez-la, ainsi que la puce (Apple ou Intel).
- [ ] L'app s'ouvre par clic droit > Ouvrir.
- [ ] L'icône de LocalTake est dans la barre des menus, en noir ou en blanc selon le thème, et pas dans le Dock.
- [ ] Le menu de l'icône affiche « LocalTake » suivi de la version (grisé) et « Quitter LocalTake », qui ferme l'app.
- [ ] Un second lancement, LocalTake déjà ouverte, ne crée pas de seconde icône.
- [ ] `brew install ffmpeg` fonctionne (ou ffmpeg est déjà là : `ffmpeg -version`).
- [ ] LocalTake fermée, dans le Terminal : `/Applications/LocalTake.app/Contents/MacOS/LocalTake --diagnostics`. Renvoyez tout ce qui s'affiche (aussi enregistré dans `~/Library/Logs/LocalTake/diagnostics.txt`). À cette étape, « screen capture » et « computer sound » peuvent être en `[FAIL]` : c'est ce que les étapes suivantes traitent.

Si la politique de votre entreprise bloque l'une de ces lignes (app non signée, Homebrew), on s'arrête là pour en reparler.

### Étape 2 : capture de l'écran

À chaque nouvelle version de l'app, macOS peut redemander les autorisations : c'est normal tant qu'elle n'est pas signée par un développeur identifié.

- [ ] Première fois, LocalTake fermée : `--diagnostics` affiche en `[FAIL]` sur « screen capture » le message « LocalTake n'a pas l'autorisation d'enregistrer l'écran… », et macOS propose l'autorisation.
- [ ] Après avoir coché LocalTake dans Réglages Système > Confidentialité et sécurité > Enregistrement de l'écran : `--diagnostics` affiche « screen capture: 64×64, N colours » avec N supérieur à 1, et « window list » un nombre de fenêtres.
- [ ] Avec un second écran branché : renvoyez aussi le rapport (les deux écrans seront listés à l'étape 5).

### Étape 3 : captures depuis la barre des menus et les raccourcis

À cette étape, « Ouvrir dans l'éditeur » (réglage par défaut) enregistre la capture dans le dossier : l'éditeur arrive à l'étape 4.

- [ ] Le menu montre les raccourcis à droite des entrées (⌃⇧1, ⌃⇧2, ⌃⇧3).
- [ ] `Ctrl+Maj+1` (zone) : l'écran se fige, assombri, et couvre aussi la barre des menus et le Dock ; la zone glissée reste claire, avec sa taille en pixels ; `Échap` annule ; un clic sans glisser prend la fenêtre sous le curseur ; `Espace` bascule en mode fenêtre.
- [ ] `Ctrl+Maj+2` (fenêtre) : la fenêtre au premier plan sous le curseur est prise, pas celle de derrière.
- [ ] `Ctrl+Maj+3` (tout l'écran), et « Capturer la dernière zone » dans le menu (la même qu'à la capture précédente ; pas de raccourci par défaut).
- [ ] Écran Retina : la capture est nette et mesure le double des points (une zone de 100 × 100 points donne 200 × 200 pixels).
- [ ] Avec un second écran : une capture dessus, et une sélection commencée sur un écran ne déborde pas sur l'autre.
- [ ] Une notification « Capture enregistrée » apparaît (macOS demande la première fois si LocalTake peut en afficher) ; un clic dessus ouvre le Finder sur le fichier.
- [ ] Réglage « Après une capture » sur « Copier » (à modifier dans `~/Library/Application Support/LocalTake/settings.json`, `"AfterCapture": "CopyToClipboard"`, LocalTake fermée) : la capture se colle dans Aperçu (Fichier > Nouveau depuis le presse-papiers).
- [ ] Un raccourci déjà pris par une autre app (par exemple en réglant `"HotkeyRegion": "Cmd+Shift+3"`) : un avertissement le liste au lancement.

### Étape 4 : éditeur

Le réglage « Après une capture » revient à « Ouvrir dans l'éditeur » (`"AfterCapture": "OpenEditor"`).

- [ ] Une capture s'ouvre dans l'éditeur, à sa taille réelle si elle tient à l'écran, nette sur un écran Retina ; « 1:1 » affiche 100 %.
- [ ] Chaque outil avec sa touche : V (sélection, déplacer, `Suppr` ou `⌫` pour effacer), A, L, R, E, P, H, T, N, B, C ; `Maj` contraint (flèche et ligne à 45°, carré, cercle, trait droit).
- [ ] Texte : `Entrée` valide, `Maj+Entrée` passe à la ligne ; `⌘C`/`⌘V` fonctionnent pendant la saisie.
- [ ] Numérotation : 1, 2, 3 ; effacer le 2 puis ajouter donne 4.
- [ ] Recadrer, puis `⌘Z` : l'image et les annotations reviennent ; `⇧⌘Z` refait.
- [ ] Flou : la zone est pixelisée à l'écran comme dans le fichier exporté.
- [ ] `⌘S` enregistre (PNG, puis JPEG en réglant `"Format": "jpg"`) ; un clic sur « Enregistré : … » montre le fichier dans le Finder ; `⇧⌘S` propose un nom et un dossier.
- [ ] `⌘C` puis collage dans Aperçu (Fichier > Nouveau depuis le presse-papiers) : l'image a la taille de la capture, quel que soit le zoom (`⌘0`, `⌘1`, `⌘=`, `⌘-`, pincement du trackpad).
- [ ] Fermer (`⌘W` ou le bouton rouge) sans enregistrer pose la question ; « Annuler » garde la fenêtre.
- [ ] « Quitter LocalTake » (menu de l'icône ou `⌘Q` dans l'éditeur) avec un éditeur non enregistré pose la question ; « Annuler » garde LocalTake ouvert.
- [ ] Menu de l'icône : « Ouvrir une image… » et « Coller depuis le presse-papiers » ouvrent l'éditeur.
- [ ] La couleur, l'épaisseur et la taille du texte choisies sont reprises à l'éditeur suivant.

### Étape 5 : vidéo

Prérequis : `brew install ffmpeg` ; pour le son de l'ordinateur, `brew install blackhole-2ch`, puis un « Périphérique à sorties multiples » (Configuration audio et MIDI) qui envoie le son à la fois aux haut-parleurs et à BlackHole.

- [ ] `--diagnostics` : la ligne « screens » liste les écrans ; collez aussi la sortie de `ffmpeg -f avfoundation -list_devices true -i ""` pour comparer l'ordre des « Capture screen N ».
- [ ] `Ctrl+Maj+4` (vidéo d'une zone) : la zone se choisit comme une capture ; un cadre rouge l'entoure ; la barre apparaît dessous (ou dessus), se déplace en la glissant, et ne prend pas le clavier à l'application filmée.
- [ ] Première vidéo avec le micro : macOS demande l'autorisation Micro pour LocalTake ; la vidéo contient la voix, jusqu'au dernier mot avant « Arrêter ».
- [ ] Avec BlackHole : le bouton « Son du Mac » est actif et le son de l'ordinateur est dans la vidéo ; sans BlackHole, il est grisé et son info-bulle dit quoi installer.
- [ ] Le cadre rouge et la barre n'apparaissent pas dans la vidéo.
- [ ] « Enregistrer une vidéo (tout l'écran) » dans le menu ; puis, avec un second écran, une vidéo d'une zone sur le second écran : c'est bien cet écran qui est filmé.
- [ ] `Ctrl+Maj+R` démarre (compte à rebours 3, 2, 1), `Ctrl+Maj+D` arrête ; « Annuler » n'enregistre rien.
- [ ] La vidéo s'ouvre dans la fenêtre d'aperçu et se lit dedans (survol de la barre de temps : aperçu des images ; plein écran) ; `Échap` ferme.
- [ ] « Afficher dans le Finder » ; « Supprimer » (après confirmation) met la vidéo à la Corbeille ; « Refaire » la met à la Corbeille et relance une vidéo de la même façon.
- [ ] « Quitter LocalTake » pendant un enregistrement demande s'il faut l'arrêter et garder la vidéo ; « Annuler » laisse l'enregistrement continuer.

### Étape 6 : réglages

- [ ] « Réglages… » ouvre la fenêtre ; pendant qu'elle est ouverte, les raccourcis de LocalTake ne déclenchent pas de capture.
- [ ] Saisie d'un raccourci avec `Cmd` et `Option` : le champ affiche par exemple `Ctrl+Option+Cmd+K` ; `⌫` le vide ; `Tab` passe au champ suivant.
- [ ] Un raccourci attribué deux fois est refusé avec un message ; un dossier relatif (`Captures`) aussi.
- [ ] « Parcourir… » pour le dossier et pour ffmpeg ; le statut d'ffmpeg dit « Détecté : … » quand le champ est vide.
- [ ] La liste des micros est remplie ; « Enregistrer le son de l'ordinateur » est grisé sans BlackHole.
- [ ] « Lancer LocalTake à l'ouverture de session » : LocalTake apparaît dans Réglages Système > Général > Ouverture, se lance après redémarrage, puis disparaît une fois la case décochée. (Si macOS refuse, le message s'affiche et le reste est enregistré.)
- [ ] Après « Enregistrer », les nouveaux raccourcis marchent et s'affichent dans le menu ; les réglages sont conservés après relance.
