# Composants tiers livrés avec LocalTake

LocalTake est livré avec les programmes et données ci-dessous. Chacun garde sa propre licence.

## ffmpeg 9.0.2

Encode les vidéos, lit le son pour la transcription. LocalTake le lance comme un programme séparé.

- Licence : GNU General Public License, version 3 (ffmpeg est compilé avec x264). Texte : `licenses/GPL-3.0.txt`.
- Code source exact : https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz
- Binaires : Windows, build « essentials » de gyan.dev (https://www.gyan.dev/ffmpeg/builds/) ; macOS, builds de
  Martin Riedl (https://ffmpeg.martin-riedl.de/) pour Apple silicon et Intel, réunis en un binaire universel.
- Site : https://ffmpeg.org

## whisper.cpp et Whisper.net

Transcrivent le micro en sous-titres, sur l'ordinateur.

- Licence : MIT.
- Code source : https://github.com/ggerganov/whisper.cpp et https://github.com/sandrohanea/whisper.net

## Modèle Whisper `ggml-small-q5_1.bin`

Le modèle de transcription, livré dans le dossier `models`.

- Licence : MIT (modèles Whisper d'OpenAI, convertis par le projet whisper.cpp).
- Origine : https://huggingface.co/ggerganov/whisper.cpp
