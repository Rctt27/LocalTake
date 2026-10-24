# LocalTake

Outil de capture d'écran et d'enregistrement vidéo pour **Windows**, inspiré de TinyTake, sans compte ni cloud : les captures restent sur l'ordinateur. LocalTake n'envoie rien nulle part, pas même des statistiques ; son journal est un fichier local.

Une version **macOS** est en préparation.

## Installation

Les versions se téléchargent depuis la page [Releases](https://github.com/Rctt27/LocalTake/releases).

**Windows 10 (2004) ou 11** : `LocalTake-x.y.z-windows-x64.zip`, à extraire dans un dossier, puis lancer `LocalTake.exe`. Gardez ensemble ce qu'il contient : `LocalTake.exe`, le dossier `runtimes` (la bibliothèque de transcription) et le dossier `models` (le modèle de transcription). Il nécessite le [runtime .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (« .NET Desktop Runtime »).

L'icône de LocalTake se trouve dans la zone de notification ; un clic ouvre son menu.

## Raccourcis par défaut

| Action | Raccourci |
| --- | --- |
| Capturer une zone (un simple clic prend la fenêtre sous le curseur) | `Impr. écran` |
| Capturer une fenêtre | `Alt+Impr. écran` |
| Capturer tout l'écran | `Ctrl+Impr. écran` |
| Vidéo d'une zone | `Ctrl+Maj+Impr. écran` |
| Démarrer / arrêter l'enregistrement (actifs seulement pendant une vidéo) | `Ctrl+R` / `Ctrl+D` |
| Surligner avec la souris (maintenir, seulement pendant l'enregistrement) | `Ctrl+Espace` |

Ils se modifient dans les réglages, depuis l'icône LocalTake. On peut aussi y attribuer un raccourci à la capture de la dernière zone et à la vidéo de tout l'écran. Si Windows refuse un raccourci, c'est qu'un autre outil l'utilise déjà (TinyTake, l'Outil Capture d'écran…).

Tant que le raccourci de surlignage est maintenu, une traînée jaune translucide suit la souris et apparaît dans la vidéo ; elle s'estompe au relâchement. Il peut aussi être une touche seule (Espace, qui n'écrit alors plus rien pendant l'enregistrement) ou un bouton latéral de la souris : cliquez avec dans le champ des réglages. Les boutons supplémentaires des souris gaming s'associent à une touche `F13` à `F24` dans le logiciel du fabricant, puis à LocalTake.

## Éditeur

Outils : flèche, ligne, rectangle, ellipse, crayon, surligneur, texte, numéros, flou, recadrage, sélection pour déplacer ou supprimer. Chaque outil a sa touche (indiquée dans l'infobulle). `Maj` contraint les formes et les angles. Le recadrage reste modifiable : `Ctrl+Z` le défait sans perdre les annotations.

`Ctrl+S` enregistre dans le dossier des captures, `Ctrl+Maj+S` enregistre sous, `Ctrl+C` copie l'image, `Ctrl+Z` / `Ctrl+Y` annulent et rétablissent.

## Vidéo

Enregistrement en MP4 d'une zone ou de tout l'écran. Une fois la zone choisie, une barre permet d'activer ou de couper le micro et le son de l'ordinateur, puis un clic sur la gauche de la barre (▶ « Démarrer ») ou le raccourci de démarrage lance un compte à rebours de 3 secondes. Pendant l'enregistrement, la même zone affiche la durée : un clic dessus (■ « Arrêter ») ou le raccourci d'arrêt enregistre la vidéo. Le cadre rouge et la barre restent invisibles dans la vidéo.

La vidéo s'ouvre ensuite dans un aperçu qui la lit aussitôt : `Espace` met en pause, `←` / `→` avancent ou reculent de 10 secondes, et survoler la barre de défilement montre l'image à cet instant. On peut l'afficher dans l'Explorateur, l'envoyer à la Corbeille, ou **Refaire** : la vidéo part à la Corbeille et un nouvel enregistrement commence de la même façon.

### Sous-titres

La bascule **Sous-titres** de la barre d'enregistrement fait transcrire le micro dès la fin de l'enregistrement. Sans elle, le bouton **Générer les sous-titres**, en haut de la fenêtre de lecture, le fait après coup : il apparaît tant que la vidéo n'a pas de sous-titres, et transcrit la piste du micro gardée pendant que la fenêtre est ouverte, ou à défaut le son de la vidéo. La transcription se fait sur l'ordinateur, avec le modèle [Whisper](https://github.com/ggerganov/whisper.cpp) livré dans le dossier `models` : rien n'est envoyé. Elle prend environ un tiers de la durée de la vidéo, et attend la fin d'un enregistrement en cours.

Les sous-titres sont découpés comme pour un tutoriel : une ligne de 42 caractères au plus (environ 5 à 7 mots), coupée à une fin de phrase, une virgule ou avant « et », « qui », « sur »…, jamais après un article, chaque bloc durant de 1 à 5 secondes ; un silence d'une demi-seconde termine un bloc. À côté des temps de chaque bloc, sa vitesse de lecture passe en orange au-delà de 15 caractères par seconde et en rouge au-delà de 17, avec l'explication en infobulle.

Le sous-titre en cours s'affiche aussi sur la vidéo, en bas de l'image, sur un bandeau noir translucide qui le garde lisible quelle que soit l'image.

Ils s'affichent à gauche de la vidéo, bloc par bloc, avec les intervalles sans sous-titre entre eux. Le bloc (ou l'intervalle) de l'instant lu est surligné et reste au milieu de la liste. Un clic sur un bloc y amène la vidéo ; modifier un texte ou un temps met la lecture en pause. `Entrée` valide, `Maj+Entrée` passe à la ligne, `Échap` annule, `Ctrl+Z` / `Ctrl+Y` défont et refont. « + Ajouter » crée un sous-titre dans un intervalle.

Les sous-titres sont enregistrés à chaque modification dans un fichier `.srt` du même nom que la vidéo, que les lecteurs (VLC, Lecteur multimédia…) affichent avec elle. La langue (français, anglais ou détection automatique) et un autre modèle Whisper (fichier `ggml-*.bin`) se choisissent dans les réglages.

LocalTake ne télécharge pas ffmpeg, qu'il faut installer : `winget install Gyan.FFmpeg`, ou indiquer son emplacement dans les réglages. Le son de l'ordinateur est capté directement depuis la sortie audio par défaut, sans pilote à installer.

## Fichiers

| | Emplacement |
| --- | --- |
| Captures | `Images\LocalTake`, ou `%USERPROFILE%\Captures LocalTake` si le dossier Images est synchronisé par OneDrive |
| Sous-titres | à côté de chaque vidéo, même nom en `.srt` |
| Modèle de transcription | `models` à côté de `LocalTake.exe`, sinon `%LOCALAPPDATA%\LocalTake\models` |
| Piste du micro en attente de transcription | `%LOCALAPPDATA%\LocalTake\work`, effacée une fois transcrite |
| Réglages | `%APPDATA%\LocalTake\settings.json` |
| Journal (7 jours) | `%LOCALAPPDATA%\LocalTake\logs` |

## Un problème ?

`LocalTake.exe --diagnostics`, dans un terminal, vérifie sans ouvrir de fenêtre ce dont LocalTake a besoin : ffmpeg, les périphériques audio, la capture d'écran, la transcription, les raccourcis. Quittez LocalTake avant, sinon ses propres raccourcis apparaissent comme déjà pris. Le rapport est aussi enregistré dans `diagnostics.txt`, dans le dossier du journal : joignez-le, avec le journal, à votre signalement.

## Développement

Nécessite le SDK .NET 10 (`global.json`). L'architecture, les commandes et les règles du dépôt sont décrites dans [CLAUDE.md](CLAUDE.md).

```
dotnet build LocalTake.slnx
dotnet test --solution LocalTake.slnx
dotnet run --project src/LocalTake.Windows
```
