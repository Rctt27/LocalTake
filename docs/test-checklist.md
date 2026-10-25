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
- [ ] Sur un PC sans ffmpeg installé : le zip extrait enregistre une vidéo (ffmpeg.exe est à côté de LocalTake.exe).
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
- [ ] Pendant l'enregistrement, la barre montre « Surligner : maintenir Ctrl+Space ». Maintenir `Ctrl+Espace` en survolant un bouton : une traînée jaune translucide suit la souris, rien n'est tapé dans l'application ; au relâchement, elle reste 0,3 s puis s'estompe en une demi-seconde environ ; un texte surligné reste lisible.
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

Avec `LocalTake-x.y.z-macos.zip` d'une release, ou l'artefact `LocalTake-macos` de la CI (onglet *Actions* du dépôt, dernier passage de la branche, section *Artifacts*). Les étapes suivent l'ordre de construction de l'app (`docs/superpowers/plans/2026-10-17-macos-native.md`) ; ensemble, elles forment la checklist macOS complète d'une release.

Pour l'installer : décompresser `LocalTake-macos.zip`, glisser `LocalTake.app` dans Applications, puis clic droit > Ouvrir > Ouvrir (l'app n'est pas signée par un développeur identifié ; macOS ne le demande qu'une fois). Si macOS refuse sans proposer « Ouvrir » : Réglages Système > Confidentialité et sécurité > « Ouvrir quand même ».

### Étape 1 : l'app démarre

- [ ] Version de macOS (menu Pomme > À propos de ce Mac) : 14 (Sonoma) ou plus récente. Notez-la, ainsi que la puce (Apple ou Intel).
- [ ] L'app s'ouvre par clic droit > Ouvrir.
- [ ] L'icône de LocalTake est dans la barre des menus, en noir ou en blanc selon le thème, et pas dans le Dock.
- [ ] Le menu de l'icône affiche « LocalTake » suivi de la version (grisé) et « Quitter LocalTake », qui ferme l'app.
- [ ] Un second lancement, LocalTake déjà ouverte, ne crée pas de seconde icône.
- [ ] Sur un Mac sans Homebrew ni ffmpeg : l'app ne demande rien à installer (`/Applications/LocalTake.app/Contents/MacOS/ffmpeg -version` affiche ffmpeg 9.0.2).
- [ ] LocalTake fermée, dans le Terminal : `open -a LocalTake --args --diagnostics`, puis `open ~/Library/Logs/LocalTake/diagnostics.txt` : renvoyez ce rapport. (Lancé ainsi, c'est bien LocalTake qui demande les autorisations ; lancé directement depuis `Contents/MacOS`, ce serait le Terminal.) À cette étape, « screen capture » et « computer sound » peuvent être en `[FAIL]` : c'est ce que les étapes suivantes traitent.

Si la politique de votre entreprise bloque l'une de ces lignes (app non signée par un développeur identifié), on s'arrête là pour en reparler.

- [ ] Parcours d'un utilisateur : zip téléchargé avec le navigateur (il est mis en quarantaine), décompressé et ouvert par clic droit > Ouvrir **sans le déplacer** ; une vidéo avec le micro s'enregistre, macOS attribue les demandes d'autorisation à LocalTake, et la ligne ffmpeg de `open -a LocalTake --args --diagnostics` pointe dans `…/AppTranslocation/…/LocalTake.app/Contents/MacOS/ffmpeg`.
- [ ] Mise à jour : après avoir installé une nouvelle version (signée avec le certificat LocalTake) par-dessus l'ancienne, l'Enregistrement de l'écran et le Micro n'ont pas à être redonnés.

### Étape 2 : capture de l'écran

À chaque nouvelle version de l'app, macOS peut redemander les autorisations : c'est normal tant qu'elle n'est pas signée par un développeur identifié.

- [ ] Première capture (`Ctrl+Maj+1` ou le menu) : un message dit « LocalTake n'a pas l'autorisation d'enregistrer l'écran… » et où l'accorder, et macOS propose l'autorisation. Une vidéo de tout l'écran donne le même message au lieu d'une vidéo noire.
- [ ] Après avoir coché LocalTake dans Réglages Système > Confidentialité et sécurité > Enregistrement de l'écran puis relancé LocalTake, la capture fonctionne ; LocalTake fermée, `open -a LocalTake --args --diagnostics` écrit dans `diagnostics.txt` « screen capture: 64×64, N colours » avec N supérieur à 1, et « window list » un nombre de fenêtres.
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

- [ ] Avec deux écrans : `open -a LocalTake --args --diagnostics` ; la ligne « screens » de `diagnostics.txt` liste les écrans dans l'ordre où ffmpeg les numérote (« Capture screen 0 », « 1 »…, visibles avec `/Applications/LocalTake.app/Contents/MacOS/ffmpeg -f avfoundation -list_devices true -i ""`).
- [ ] `Ctrl+Maj+4` (vidéo d'une zone) : la zone se choisit comme une capture ; un cadre rouge l'entoure ; la barre apparaît dessous (ou dessus), se déplace en la glissant, et ne prend pas le clavier à l'application filmée.
- [ ] Première vidéo avec le micro : macOS demande l'autorisation Micro pour LocalTake ; la vidéo contient la voix, jusqu'au dernier mot avant « Arrêter ».
- [ ] Son du Mac sans pilote : sur un Mac sans BlackHole, le bouton « Son du Mac » est actif ; une vidéo ou une musique jouée pendant l'enregistrement s'entend dans le MP4.
- [ ] Synchronisation du son du Mac : `FFMPEG=/Applications/LocalTake.app/Contents/MacOS/ffmpeg bash build/sync-check.sh clip ~/Desktop/sync.mp4` (depuis le dépôt), lire `sync.mp4` en plein écran dans QuickTime, l'enregistrer avec LocalTake (vidéo de tout l'écran, son du Mac seul, micro coupé), puis `FFMPEG=… bash build/sync-check.sh measure <la vidéo>` : chaque écart est dans ±0,1 s. Sinon, noter l'écart moyen affiché et le signaler.
- [ ] Silence : 10 s enregistrées sans rien jouer, avec le micro et le son du Mac : la piste dure autant que la vidéo et la voix garde son niveau.
- [ ] Capture du son en échec (le journal `~/Library/Logs/LocalTake` le dit) : la vidéo s'enregistre quand même, avec un son du Mac muet de la durée de la vidéo.
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
- [ ] La liste des micros est remplie (sans BlackHole s'il est installé) ; « Enregistrer le son de l'ordinateur » est actif, sans pilote.
- [ ] « Lancer LocalTake à l'ouverture de session » : LocalTake apparaît dans Réglages Système > Général > Ouverture, se lance après redémarrage, puis disparaît une fois la case décochée. (Si macOS refuse, le message s'affiche et le reste est enregistré.)
- [ ] Après « Enregistrer », les nouveaux raccourcis marchent et s'affichent dans le menu ; les réglages sont conservés après relance.
