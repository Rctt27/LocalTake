# LocalTake

Outil de capture d'écran et d'enregistrement vidéo pour Windows, inspiré de TinyTake, sans compte ni cloud : les captures restent sur l'ordinateur.

## Raccourcis par défaut

| Raccourci | Action |
| --- | --- |
| `Impr. écran` | Capturer une zone (un simple clic prend la fenêtre sous le curseur) |
| `Alt+Impr. écran` | Capturer une fenêtre |
| `Ctrl+Impr. écran` | Capturer tout l'écran |
| `Ctrl+Maj+Impr. écran` | Vidéo d'une zone |
| `Ctrl+R` / `Ctrl+D` | Démarrer / arrêter l'enregistrement (actifs seulement pendant une vidéo) |

Ils se modifient dans les réglages, depuis l'icône de la zone de notification. On peut aussi y attribuer un raccourci à la capture de la dernière zone et à la vidéo de tout l'écran.

Si Windows refuse un raccourci, c'est qu'un autre outil l'utilise déjà (TinyTake, l'Outil Capture d'écran…).

## Éditeur

Outils : flèche, ligne, rectangle, ellipse, crayon, surligneur, texte, numéros, flou, recadrage, sélection pour déplacer ou supprimer. Chaque outil a sa touche (indiquée dans l'infobulle). `Maj` contraint les formes et les angles.

`Ctrl+S` enregistre dans le dossier des captures, `Ctrl+Maj+S` enregistre sous, `Ctrl+C` copie l'image, `Ctrl+Z` / `Ctrl+Y` annulent et rétablissent.

## Vidéo

Enregistrement en MP4 d'une zone ou de tout l'écran. Une fois la zone choisie, une barre permet d'activer ou couper le micro et le son de l'ordinateur, puis **Démarrer** (ou `Ctrl+R`) lance un compte à rebours de 3 secondes. **Arrêter** (ou `Ctrl+D`) enregistre la vidéo. Le cadre rouge et la barre restent invisibles dans la vidéo.

Nécessite ffmpeg, que LocalTake ne télécharge pas : `winget install Gyan.FFmpeg`, ou indiquer son emplacement dans les réglages. Le micro utilisé se choisit dans les réglages ; le son de l'ordinateur utilise le pilote `virtual-audio-capturer` installé avec TinyTake (TinyTake Filter).

## Fichiers

- Dossier des captures : `Images\LocalTake`, ou `%USERPROFILE%\Captures LocalTake` si le dossier Images est synchronisé par OneDrive.
- Réglages : `%APPDATA%\LocalTake\settings.json`.

## Compiler

Nécessite le SDK .NET 8.

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
