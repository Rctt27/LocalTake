# Tests manuels

Les tests automatisés couvrent la logique, ffmpeg et le rendu de l'éditeur. Ce qui touche à l'écran réel, aux raccourcis système et aux autorisations se vérifie à la main, avec cette liste, avant une release.

Notez pour chaque ligne : ✅, ❌ (avec ce qui s'est passé) ou — (non testé). Joignez à vos retours le rapport de `LocalTake --diagnostics` et le journal (voir README, « Fichiers »).

## Windows (application Avalonia, `src/LocalTake.App`)

À dérouler une fois avant de supprimer l'ancienne application WPF (`src/LocalTake.Wpf`).

### Démarrage

- [ ] L'icône apparaît dans la zone de notification ; un clic droit ouvre le menu, avec les raccourcis à droite des entrées.
- [ ] Un second lancement ne crée pas de seconde icône.
- [ ] Avec TinyTake lancé, un avertissement liste les raccourcis refusés.
- [ ] Un `settings.json` de la v0.1.0 est repris tel quel (raccourcis, dossier, format, compteur).

### Captures

- [ ] `Impr. écran` : l'écran se fige et s'assombrit, la zone glissée reste claire avec sa taille affichée ; `Échap` annule.
- [ ] Un simple clic prend la fenêtre sous le curseur ; `Espace` bascule en mode fenêtre.
- [ ] Sur deux écrans (si possible avec deux échelles différentes) : la sélection fonctionne sur chacun, et la capture a la taille en pixels réelle.
- [ ] `Alt+Impr. écran`, `Ctrl+Impr. écran` et « Capturer la dernière zone » fonctionnent.
- [ ] Depuis le menu, le menu lui-même n'apparaît pas dans la capture.
- [ ] Les quatre réglages « Après une capture » : éditeur, fichier (notification en bas à droite, un clic montre le fichier), presse-papier, les deux.

### Éditeur

- [ ] Chaque outil, sa touche, `Maj` pour contraindre ; texte avec `Entrée` et `Maj+Entrée`.
- [ ] Déplacer et supprimer une annotation avec l'outil Sélection.
- [ ] Recadrer, puis `Ctrl+Z` : l'image entière revient avec ses annotations.
- [ ] Flou : la zone est pixelisée, l'export aussi.
- [ ] `Ctrl+S` (PNG puis JPEG), `Ctrl+Maj+S`, `Ctrl+C` puis coller dans Paint : l'image a la taille de la capture, quel que soit le zoom.
- [ ] Fermer sans enregistrer pose la question ; « Annuler » garde l'éditeur ouvert.
- [ ] « Ouvrir une image… » et « Coller depuis le presse-papier » (avec une image et sans).

### Vidéo

- [ ] `Ctrl+Maj+Impr. écran` : zone, cadre rouge autour, barre dessous ; `Ctrl+R` démarre après 3 secondes.
- [ ] Micro et son du PC (pilote TinyTake) : `ffmpeg -i video.mp4` montre une piste audio, et la fin de la phrase prononcée avant « Arrêter » est présente.
- [ ] Le cadre et la barre n'apparaissent pas dans la vidéo.
- [ ] Aperçu : images au survol de la barre, « Lire » ouvre le lecteur, « Afficher », « Supprimer » (Corbeille), « Refaire ».
- [ ] « Annuler » pendant l'enregistrement : aucun fichier ne reste.

### Réglages

- [ ] Saisie des raccourcis (dont `Impr. écran`), « Raccourcis de TinyTake », doublon refusé.
- [ ] Dossier relatif refusé ; « Parcourir… » pour le dossier et ffmpeg.
- [ ] « Lancer LocalTake au démarrage de Windows » : présent après redémarrage, puis désactivé.
- [ ] « Quitter » pendant un enregistrement pose la question ; avec un éditeur non enregistré aussi.

## macOS

Télécharger `LocalTake-macos` depuis l'onglet **Actions** du dépôt (dernière exécution réussie de CI, section *Artifacts*), puis décompresser le zip de votre puce (arm64 pour Apple, x64 pour Intel).

### Prérequis du Mac (à vérifier en premier)

- [ ] L'application non signée s'ouvre par clic droit > Ouvrir (sinon : la politique de l'entreprise la bloque).
- [ ] L'autorisation « Enregistrement de l'écran » peut être accordée à LocalTake.
- [ ] `brew install ffmpeg` (et, pour le son de l'ordinateur, `brew install blackhole-2ch`) sont possibles.
- [ ] `LocalTake.app/Contents/MacOS/LocalTake --diagnostics` dans le Terminal : coller le rapport dans vos retours.

### Fonctionnement

- [ ] L'icône apparaît dans la barre des menus, pas dans le Dock ; le menu s'ouvre.
- [ ] `Ctrl+Maj+1` : l'overlay couvre aussi la barre des menus et le Dock ; sélection d'une zone, d'une fenêtre (clic).
- [ ] Écran Retina : la capture est nette et a la taille en pixels (le double des points).
- [ ] Second écran, si possible : sélection et capture dessus.
- [ ] Éditeur : outils, `Cmd+S`, `Cmd+C` puis coller dans Aperçu (Fichier > Nouveau à partir du presse-papiers), `Cmd+Z`.
- [ ] Vidéo d'une zone (`Ctrl+Maj+4`) : autorisation Micro demandée ; vidéo lisible dans QuickTime avec le son.
- [ ] Avec BlackHole configuré : le son de l'ordinateur est dans la vidéo.
- [ ] Le cadre rouge et la barre n'apparaissent pas dans la vidéo.
- [ ] Aperçu : « Lire » ouvre QuickTime ; « Afficher » ouvre le Finder sur le fichier ; « Supprimer » le met dans la Corbeille.
- [ ] Réglages : raccourcis avec `Cmd`/`Option`, « Lancer LocalTake à l'ouverture de session » (fichier dans `~/Library/LaunchAgents`), puis désactivé.
