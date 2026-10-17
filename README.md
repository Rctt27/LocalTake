# LocalTake

Outil de capture d'écran et d'enregistrement vidéo pour **Windows** et **macOS**, inspiré de TinyTake, sans compte ni cloud : les captures restent sur l'ordinateur. LocalTake n'envoie rien nulle part, pas même des statistiques ; son journal est un fichier local.

## Installation

Les versions se téléchargent depuis la page [Releases](https://github.com/Rctt27/LocalTake/releases).

- **Windows 10 (2004) ou 11** : `LocalTake-x.y.z-windows-x64.exe`, à lancer tel quel. Il nécessite le [runtime .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (« .NET Desktop Runtime » ou « .NET Runtime »).
- **macOS 12 ou plus récent** : `LocalTake-x.y.z-macos-arm64.zip` (puces Apple) ou `-macos-x64.zip` (Intel). Décompressez, glissez **LocalTake** dans Applications, puis faites **clic droit > Ouvrir** au premier lancement : l'application n'est pas notarisée par Apple. macOS demande ensuite l'autorisation **Enregistrement de l'écran** (Réglages Système > Confidentialité et sécurité), puis **Micro** à la première vidéo avec le micro.

L'icône de LocalTake se trouve dans la zone de notification (Windows) ou la barre des menus (macOS).

## Raccourcis par défaut

| Action | Windows | macOS |
| --- | --- | --- |
| Capturer une zone (un simple clic prend la fenêtre sous le curseur) | `Impr. écran` | `Ctrl+Maj+1` |
| Capturer une fenêtre | `Alt+Impr. écran` | `Ctrl+Maj+2` |
| Capturer tout l'écran | `Ctrl+Impr. écran` | `Ctrl+Maj+3` |
| Vidéo d'une zone | `Ctrl+Maj+Impr. écran` | `Ctrl+Maj+4` |
| Démarrer / arrêter l'enregistrement (actifs seulement pendant une vidéo) | `Ctrl+R` / `Ctrl+D` | `Ctrl+Maj+R` / `Ctrl+Maj+D` |

Ils se modifient dans les réglages, depuis l'icône LocalTake. On peut aussi y attribuer un raccourci à la capture de la dernière zone et à la vidéo de tout l'écran. Si le système refuse un raccourci, c'est qu'un autre outil l'utilise déjà (TinyTake, l'Outil Capture d'écran…).

## Éditeur

Outils : flèche, ligne, rectangle, ellipse, crayon, surligneur, texte, numéros, flou, recadrage, sélection pour déplacer ou supprimer. Chaque outil a sa touche (indiquée dans l'infobulle). `Maj` contraint les formes et les angles. Le recadrage reste modifiable : `Ctrl+Z` le défait sans perdre les annotations.

`Ctrl+S` enregistre dans le dossier des captures, `Ctrl+Maj+S` enregistre sous, `Ctrl+C` copie l'image, `Ctrl+Z` / `Ctrl+Y` annulent et rétablissent. Sur Mac, `Cmd` remplace `Ctrl`.

## Vidéo

Enregistrement en MP4 d'une zone ou de tout l'écran. Une fois la zone choisie, une barre permet d'activer ou de couper le micro et le son de l'ordinateur, puis **Démarrer** lance un compte à rebours de 3 secondes. **Arrêter** enregistre la vidéo. Le cadre rouge et la barre restent invisibles dans la vidéo.

La vidéo s'ouvre ensuite dans un aperçu : la barre de défilement montre ses images (`←` / `→` : 10 secondes), **Lire** (ou `Espace`) l'ouvre dans le lecteur vidéo du système. On peut l'afficher dans le dossier, l'envoyer à la Corbeille, ou **Refaire** : la vidéo part à la Corbeille et un nouvel enregistrement commence de la même façon.

LocalTake ne télécharge pas ffmpeg, qu'il faut installer :

- Windows : `winget install Gyan.FFmpeg`. Le son de l'ordinateur utilise le pilote `virtual-audio-capturer` installé avec TinyTake (TinyTake Filter).
- macOS : `brew install ffmpeg`. Le son de l'ordinateur utilise le pilote gratuit BlackHole : `brew install blackhole-2ch`, puis, dans « Configuration audio et MIDI », un périphérique à sortie multiple regroupant vos haut-parleurs et BlackHole.

On peut aussi indiquer l'emplacement de ffmpeg dans les réglages.

## Fichiers

| | Windows | macOS |
| --- | --- | --- |
| Captures | `Images\LocalTake`, ou `%USERPROFILE%\Captures LocalTake` si le dossier Images est synchronisé par OneDrive | `~/Pictures/LocalTake` |
| Réglages | `%APPDATA%\LocalTake\settings.json` | `~/Library/Application Support/LocalTake/settings.json` |
| Journal (7 jours) | `%LOCALAPPDATA%\LocalTake\logs` | `~/Library/Logs/LocalTake` |

## Un problème ?

`LocalTake --diagnostics` vérifie, sans ouvrir de fenêtre, ce dont LocalTake a besoin : ffmpeg, les périphériques audio, la capture d'écran, les raccourcis. Sur Mac : `/Applications/LocalTake.app/Contents/MacOS/LocalTake --diagnostics` dans le Terminal. Joignez ce rapport et le journal à votre signalement.

## Développement

Nécessite le SDK .NET 10 (`global.json`). L'architecture, les commandes et les règles du dépôt sont décrites dans [CLAUDE.md](CLAUDE.md).

```
dotnet build LocalTake.slnx
dotnet test --solution LocalTake.slnx
dotnet run --project src/LocalTake.Windows
```
