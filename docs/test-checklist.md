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

L'application macOS se construit par étapes (`docs/superpowers/plans/2026-10-17-macos-native.md`). Chaque étape ajoute sa partie ci-dessous ; on la vérifie avec l'artefact `LocalTake-macos` de la CI (onglet *Actions* du dépôt, dernier passage de la branche, section *Artifacts*).

Pour l'installer : décompresser `LocalTake-macos.zip`, glisser `LocalTake.app` dans Applications, puis clic droit > Ouvrir > Ouvrir (l'app n'est pas signée par un développeur identifié ; macOS ne le demande qu'une fois). Si macOS refuse sans proposer « Ouvrir » : Réglages Système > Confidentialité et sécurité > « Ouvrir quand même ».

### Étape 1 : l'app démarre

- [ ] Version de macOS (menu Pomme > À propos de ce Mac) : 14 (Sonoma) ou plus récente. Notez-la, ainsi que la puce (Apple ou Intel).
- [ ] L'app s'ouvre par clic droit > Ouvrir.
- [ ] L'icône de LocalTake est dans la barre des menus, en noir ou en blanc selon le thème, et pas dans le Dock.
- [ ] Le menu de l'icône affiche « LocalTake 0.2.0 » (grisé) et « Quitter LocalTake », qui ferme l'app.
- [ ] Un second lancement, LocalTake déjà ouverte, ne crée pas de seconde icône.
- [ ] `brew install ffmpeg` fonctionne (ou ffmpeg est déjà là : `ffmpeg -version`).
- [ ] LocalTake fermée, dans le Terminal : `/Applications/LocalTake.app/Contents/MacOS/LocalTake --diagnostics`. Renvoyez tout ce qui s'affiche (aussi enregistré dans `~/Library/Logs/LocalTake/diagnostics.txt`). À cette étape, « screen capture » et « computer sound » peuvent être en `[FAIL]` : c'est ce que les étapes suivantes traitent.

Si la politique de votre entreprise bloque l'une de ces lignes (app non signée, Homebrew), on s'arrête là pour en reparler.

### Étape 2 : capture de l'écran

À chaque nouvelle version de l'app, macOS peut redemander les autorisations : c'est normal tant qu'elle n'est pas signée par un développeur identifié.

- [ ] Première fois, LocalTake fermée : `--diagnostics` affiche en `[FAIL]` sur « screen capture » le message « LocalTake n'a pas l'autorisation d'enregistrer l'écran… », et macOS propose l'autorisation.
- [ ] Après avoir coché LocalTake dans Réglages Système > Confidentialité et sécurité > Enregistrement de l'écran : `--diagnostics` affiche « screen capture: 64×64, N colours » avec N supérieur à 1, et « window list » un nombre de fenêtres.
- [ ] Avec un second écran branché : renvoyez aussi le rapport (les deux écrans seront listés à l'étape 5).
