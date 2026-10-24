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
- [ ] La gauche de la barre est le bouton marche/arrêt : prête, elle montre ▶ « Prêt à enregistrer · Démarrer (Ctrl+R) » et un clic lance le compte à rebours ; pendant le compte à rebours, un point grisé et pas de clic ; pendant l'enregistrement, ■ avec la durée et « Arrêter (Ctrl+D) », et un clic arrête et enregistre la vidéo (y compris pendant « Démarrage… ») ; pendant « Finalisation… », pas de clic.
- [ ] Micro et son du PC, sans le pilote TinyTake : `ffmpeg -i video.mp4` montre une piste audio, le son joué est synchrone avec l'image, et la fin de la phrase prononcée avant « Arrêter » est présente.
- [ ] Son du PC sans rien qui joue : l'aperçu dure autant que l'enregistrement.
- [ ] Casque USB débranché pendant l'enregistrement : la vidéo se finalise, le son du PC devient muet jusqu'à la fin.
- [ ] Annuler pendant « Démarrage… », puis réenregistrer : rien ne bloque.
- [ ] Sortie audio désactivée dans Windows : « Son du PC » est grisé, l'infobulle dit qu'il faut une sortie audio.
- [ ] Le cadre et la barre n'apparaissent pas dans la vidéo.
- [ ] Aperçu : la vidéo se lit dans la fenêtre ; `Espace`, `←` / `→` ; image au survol de la barre de défilement.
- [ ] « Afficher dans l'Explorateur » ouvre l'Explorateur sur la vidéo.
- [ ] « Supprimer » pendant la lecture : la vidéo part dans la Corbeille (et peut en être restaurée).
- [ ] « Refaire » : la vidéo part dans la Corbeille et un nouvel enregistrement commence.
- [ ] « Annuler » pendant l'enregistrement : aucun fichier ne reste.
- [ ] Pendant l'enregistrement, la barre montre « Surligner : maintenir Ctrl+Space ». Maintenir `Ctrl+Espace` en survolant un bouton : une traînée jaune translucide suit la souris, rien n'est tapé dans l'application ; au relâchement, elle s'estompe en une demi-seconde environ.
- [ ] Lâcher `Ctrl` avant `Espace` : la traînée s'estompe tout de suite. `Espace` seul tape un espace dans un formulaire de la démo.
- [ ] Pendant le compte à rebours et après « Arrêter », `Ctrl+Espace` ne surligne pas.
- [ ] Dans le MP4 : la traînée est visible et son fondu fluide (`ffmpeg -i video.mp4 -vf fps=10 images/%03d.png`).
- [ ] Deux écrans et 150 % : la traînée est sous la souris, de la même épaisseur apparente ; hors de la zone, rien ne s'affiche.
- [ ] Réglages : un bouton latéral de souris cliqué dans « Surligner (maintenir) » donne `XButton1`/`XButton2` ; en enregistrement, il surligne et un navigateur ne fait pas « Précédent ». `Espace` seul est accepté, avec l'avertissement sous le champ.

### Sous-titres

Avec le zip de la release (ou l'exe publié, avec un modèle `ggml-*.bin` dans un dossier `models` à côté).

- [ ] Bascule « Sous-titres » : grisée quand le micro est coupé ou sans modèle, avec l'infobulle qui l'explique ; le choix est retenu d'une vidéo à l'autre.
- [ ] Enregistrer une minute en parlant, avec « Sous-titres » : l'aperçu s'ouvre et lit la vidéo aussitôt ; le panneau de gauche montre « Transcription du micro… » avec sa progression, puis les blocs.
- [ ] Les blocs font une ligne de 5 à 7 mots environ, coupée à un endroit naturel (jamais après « le », « de », « je »…) ; une pause d'une demi-seconde commence un nouveau bloc ; la vitesse de lecture s'affiche à côté des temps (orange au-delà de 15 car./s, rouge au-delà de 17).
- [ ] Le sous-titre en cours s'affiche sur la vidéo, en bas de l'image (pas dans les bandes noires), sur un bandeau noir translucide, à une taille proportionnée à l'image ; rien entre deux blocs ; un texte corrigé s'y met à jour.
- [ ] Pendant la lecture, le bloc surligné suit la voix ; entre deux phrases, c'est l'intervalle « aucun sous-titre » qui est surligné ; la liste défile pour le garder au milieu.
- [ ] Clic sur la barre de défilement de la vidéo, `←` / `→` : le panneau se place aussitôt sur le bloc ou l'intervalle de ce moment.
- [ ] Clic sur un bloc ou un intervalle : la vidéo y va.
- [ ] Cliquer dans un texte met la lecture en pause ; `Espace` y tape une espace (sans relancer la vidéo) ; `Entrée` valide ; `Maj+Entrée` passe à la ligne ; `Échap` remet le texte d'avant.
- [ ] Un début avant la fin du bloc précédent est refusé avec un message, et l'ancien temps revient. Raccourcir une fin fait apparaître un intervalle.
- [ ] « + Ajouter » dans un intervalle, supprimer un bloc, `Ctrl+Z` / `Ctrl+Y` hors des champs.
- [ ] Le `.srt` à côté de la vidéo reprend les modifications ; VLC l'affiche avec la vidéo, accents compris.
- [ ] Sans la bascule « Sous-titres » : la fenêtre de lecture montre « Générer les sous-titres » en haut ; un clic ouvre le panneau avec la progression, puis les blocs ; le bouton disparaît pendant la transcription et quand il y a des sous-titres, et revient si rien n'a été entendu.
- [ ] Fermer la fenêtre sans générer : `%LOCALAPPDATA%\LocalTake\work` est vide. Une vidéo avec un `.srt` vide affiche aussi le bouton.
- [ ] Lancer un nouvel enregistrement pendant une transcription : elle s'interrompt (« en attente ») et reprend après.
- [ ] « Supprimer » ou « Refaire » pendant la transcription : la vidéo et le `.srt` partent à la Corbeille, la transcription s'arrête.
- [ ] Ne rien dire : « Aucune parole détectée », pas de `.srt`.
- [ ] `ffmpeg -i video.mp4` : la vidéo n'a pas changé (une piste vidéo, une piste audio), et `%LOCALAPPDATA%\LocalTake\work` est vide après la transcription.

### Réglages

- [ ] Saisie des raccourcis (dont `Impr. écran`), « Raccourcis de TinyTake », doublon refusé avec un message (y compris le surlignage identique à Démarrer).
- [ ] Dossier relatif refusé ; « Parcourir… » pour le dossier et ffmpeg ; un chemin ffmpeg inexistant est refusé.
- [ ] Sous-titres : le modèle livré est affiché ; un modèle inexistant est refusé ; la langue est retenue.
- [ ] « Lancer LocalTake au démarrage de Windows » : présent après redémarrage, puis désactivé.
- [ ] « Quitter » pendant un enregistrement pose la question ; avec un éditeur non enregistré aussi.

## macOS

L'application macOS est en préparation ; sa checklist arrivera avec elle (voir `docs/superpowers/plans/2026-10-17-macos-native.md`).
