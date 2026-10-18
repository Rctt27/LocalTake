# LocalTake

Outil de capture d'écran et d'enregistrement vidéo pour **Windows** et **macOS**, inspiré de TinyTake, sans compte ni cloud : les captures restent sur l'ordinateur. LocalTake n'envoie rien nulle part, pas même des statistiques ; son journal est un fichier local.

## Installation

Les versions se téléchargent depuis la page [Releases](https://github.com/Rctt27/LocalTake/releases).

**Windows 10 (2004) ou 11** : `LocalTake-x.y.z-windows-x64.exe`, à lancer tel quel. Il nécessite le [runtime .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (« .NET Desktop Runtime »). L'icône de LocalTake se trouve dans la zone de notification ; un clic ouvre son menu.

**macOS 14 (Sonoma) ou plus récent**, puce Apple ou Intel : `LocalTake-x.y.z-macos.zip`. Décompressez-le, glissez `LocalTake.app` dans Applications, puis ouvrez-le par **clic droit > Ouvrir** : l'app n'est pas signée par un développeur identifié, macOS le demande une fois (sinon : Réglages Système > Confidentialité et sécurité > « Ouvrir quand même »). L'icône de LocalTake se trouve dans la barre des menus. macOS demande ensuite deux autorisations, à accorder dans Réglages Système > Confidentialité et sécurité :
- **Enregistrement de l'écran**, à la première capture ; LocalTake doit être relancé après l'avoir accordée ;
- **Micro**, à la première vidéo avec le micro.

## Raccourcis par défaut

| Action | Windows | macOS |
| --- | --- | --- |
| Capturer une zone (un simple clic prend la fenêtre sous le curseur) | `Impr. écran` | `Ctrl+Maj+1` |
| Capturer une fenêtre | `Alt+Impr. écran` | `Ctrl+Maj+2` |
| Capturer tout l'écran | `Ctrl+Impr. écran` | `Ctrl+Maj+3` |
| Vidéo d'une zone | `Ctrl+Maj+Impr. écran` | `Ctrl+Maj+4` |
| Démarrer / arrêter l'enregistrement (actifs seulement pendant une vidéo) | `Ctrl+R` / `Ctrl+D` | `Ctrl+Maj+R` / `Ctrl+Maj+D` |

Un Mac n'a pas de touche Impr. écran, et `Cmd+Maj+3/4/5` appartiennent à macOS. Les raccourcis se modifient dans les réglages, depuis l'icône LocalTake. On peut aussi y attribuer un raccourci à la capture de la dernière zone et à la vidéo de tout l'écran. Si le système refuse un raccourci, c'est qu'un autre outil l'utilise déjà (TinyTake, l'Outil Capture d'écran…).

## Éditeur

Outils : flèche, ligne, rectangle, ellipse, crayon, surligneur, texte, numéros, flou, recadrage, sélection pour déplacer ou supprimer. Chaque outil a sa touche (indiquée dans l'infobulle). `Maj` contraint les formes et les angles. Le recadrage reste modifiable : annuler le défait sans perdre les annotations.

| | Windows | macOS |
| --- | --- | --- |
| Enregistrer dans le dossier des captures | `Ctrl+S` | `Cmd+S` |
| Enregistrer sous | `Ctrl+Maj+S` | `Cmd+Maj+S` |
| Copier l'image | `Ctrl+C` | `Cmd+C` |
| Annuler / rétablir | `Ctrl+Z` / `Ctrl+Y` | `Cmd+Z` / `Cmd+Maj+Z` |

## Vidéo

Enregistrement en MP4 d'une zone ou de tout l'écran. Une fois la zone choisie, une barre permet d'activer ou de couper le micro et le son de l'ordinateur, puis **Démarrer** lance un compte à rebours de 3 secondes. **Arrêter** enregistre la vidéo. Le cadre rouge et la barre restent invisibles dans la vidéo.

La vidéo s'ouvre ensuite dans un aperçu qui la lit aussitôt, avec l'image à chaque instant au survol de la barre de défilement. On peut l'afficher dans l'Explorateur ou le Finder, l'envoyer à la Corbeille, ou **Refaire** : la vidéo part à la Corbeille et un nouvel enregistrement commence de la même façon.

LocalTake ne télécharge pas ffmpeg, qu'il faut installer, ou dont on indique l'emplacement dans les réglages :
- Windows : `winget install Gyan.FFmpeg` ; le son de l'ordinateur utilise le pilote `virtual-audio-capturer` installé avec TinyTake (TinyTake Filter) ;
- macOS : `brew install ffmpeg` ; le son de l'ordinateur passe par BlackHole (`brew install blackhole-2ch`), avec un « Périphérique à sorties multiples » (Configuration audio et MIDI) qui envoie le son à la fois aux haut-parleurs et à BlackHole.

## Fichiers

| | Windows | macOS |
| --- | --- | --- |
| Captures | `Images\LocalTake`, ou `%USERPROFILE%\Captures LocalTake` si le dossier Images est synchronisé par OneDrive | `~/Pictures/LocalTake` |
| Réglages | `%APPDATA%\LocalTake\settings.json` | `~/Library/Application Support/LocalTake/settings.json` |
| Journal (7 jours) | `%LOCALAPPDATA%\LocalTake\logs` | `~/Library/Logs/LocalTake` |

## Un problème ?

Le diagnostic vérifie, sans ouvrir de fenêtre, ce dont LocalTake a besoin : ffmpeg, les périphériques audio, la capture d'écran, les raccourcis, et sur Mac les écrans. Quittez LocalTake avant, sinon ses propres raccourcis apparaissent comme déjà pris. Dans un terminal :
- Windows : `LocalTake.exe --diagnostics` ;
- macOS : `/Applications/LocalTake.app/Contents/MacOS/LocalTake --diagnostics`.

Le rapport est aussi enregistré dans `diagnostics.txt`, dans le dossier du journal : joignez-le, avec le journal, à votre signalement.

## Développement

Nécessite le SDK .NET 10 (`global.json`). L'architecture, les commandes et les règles du dépôt sont décrites dans [CLAUDE.md](CLAUDE.md).

```
dotnet build LocalTake.slnx
dotnet test --solution LocalTake.slnx
dotnet run --project src/LocalTake.Windows
```

L'app macOS se construit sur un Mac avec Xcode et le workload `macos` (voir CLAUDE.md) :

```
dotnet build src/LocalTake.Mac
```
