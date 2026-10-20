# LocalTake

Outil de capture d'écran et d'enregistrement vidéo pour **Windows**, inspiré de TinyTake, sans compte ni cloud : les captures restent sur l'ordinateur. LocalTake n'envoie rien nulle part, pas même des statistiques ; son journal est un fichier local.

Une version **macOS** est en préparation.

## Installation

Les versions se téléchargent depuis la page [Releases](https://github.com/Rctt27/LocalTake/releases).

**Windows 10 (2004) ou 11** : `LocalTake-x.y.z-windows-x64.exe`, à lancer tel quel. Il nécessite le [runtime .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (« .NET Desktop Runtime »).

L'icône de LocalTake se trouve dans la zone de notification ; un clic ouvre son menu.

## Raccourcis par défaut

| Action | Raccourci |
| --- | --- |
| Capturer une zone (un simple clic prend la fenêtre sous le curseur) | `Impr. écran` |
| Capturer une fenêtre | `Alt+Impr. écran` |
| Capturer tout l'écran | `Ctrl+Impr. écran` |
| Vidéo d'une zone | `Ctrl+Maj+Impr. écran` |
| Démarrer / arrêter l'enregistrement (actifs seulement pendant une vidéo) | `Ctrl+R` / `Ctrl+D` |

Ils se modifient dans les réglages, depuis l'icône LocalTake. On peut aussi y attribuer un raccourci à la capture de la dernière zone et à la vidéo de tout l'écran. Si Windows refuse un raccourci, c'est qu'un autre outil l'utilise déjà (TinyTake, l'Outil Capture d'écran…).

## Éditeur

Outils : flèche, ligne, rectangle, ellipse, crayon, surligneur, texte, numéros, flou, recadrage, sélection pour déplacer ou supprimer. Chaque outil a sa touche (indiquée dans l'infobulle). `Maj` contraint les formes et les angles. Le recadrage reste modifiable : `Ctrl+Z` le défait sans perdre les annotations.

`Ctrl+S` enregistre dans le dossier des captures, `Ctrl+Maj+S` enregistre sous, `Ctrl+C` copie l'image, `Ctrl+Z` / `Ctrl+Y` annulent et rétablissent.

## Vidéo

Enregistrement en MP4 d'une zone ou de tout l'écran. Une fois la zone choisie, une barre permet d'activer ou de couper le micro et le son de l'ordinateur, puis **Démarrer** lance un compte à rebours de 3 secondes. **Arrêter** enregistre la vidéo. Le cadre rouge et la barre restent invisibles dans la vidéo.

La vidéo s'ouvre ensuite dans un aperçu qui la lit aussitôt : `Espace` met en pause, `←` / `→` avancent ou reculent de 10 secondes, et survoler la barre de défilement montre l'image à cet instant. On peut l'afficher dans l'Explorateur, l'envoyer à la Corbeille, ou **Refaire** : la vidéo part à la Corbeille et un nouvel enregistrement commence de la même façon.

LocalTake ne télécharge pas ffmpeg, qu'il faut installer : `winget install Gyan.FFmpeg`, ou indiquer son emplacement dans les réglages. Le son de l'ordinateur est capté directement depuis la sortie audio par défaut, sans pilote à installer.

## Fichiers

| | Emplacement |
| --- | --- |
| Captures | `Images\LocalTake`, ou `%USERPROFILE%\Captures LocalTake` si le dossier Images est synchronisé par OneDrive |
| Réglages | `%APPDATA%\LocalTake\settings.json` |
| Journal (7 jours) | `%LOCALAPPDATA%\LocalTake\logs` |

## Un problème ?

`LocalTake.exe --diagnostics`, dans un terminal, vérifie sans ouvrir de fenêtre ce dont LocalTake a besoin : ffmpeg, les périphériques audio, la capture d'écran, les raccourcis. Quittez LocalTake avant, sinon ses propres raccourcis apparaissent comme déjà pris. Le rapport est aussi enregistré dans `diagnostics.txt`, dans le dossier du journal : joignez-le, avec le journal, à votre signalement.

## Développement

Nécessite le SDK .NET 10 (`global.json`). L'architecture, les commandes et les règles du dépôt sont décrites dans [CLAUDE.md](CLAUDE.md).

```
dotnet build LocalTake.slnx
dotnet test --solution LocalTake.slnx
dotnet run --project src/LocalTake.Windows
```
