# Livrer ShowTime sans dépendance (ffmpeg embarqué, signature stable) — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** les zips Windows et macOS contiennent ffmpeg (épinglé, vérifié), ShowTime le trouve tout seul, et l'app Mac est signée avec un certificat stable pour que macOS garde ses autorisations d'une version à l'autre.

**Architecture:** un script `build/fetch-ffmpeg.sh` récupère la version épinglée de ffmpeg par plateforme et la vérifie (SHA-256, x264, entrées de capture) ; les jobs CI et release l'utilisent pour les zips et aussi pour les tests, qui tournent ainsi sur le binaire livré. Un script `build/package-macos.sh` (commun à la CI et à la release) place ffmpeg dans `ShowTime.app/Contents/MacOS`, signe (certificat des secrets, ou ad hoc sans eux) et zippe. Côté code, seuls changent l'endroit où le Mac cherche ffmpeg et les messages qui disaient de l'installer.

**Tech Stack:** bash (runners GitHub windows/macos), GitHub Actions, `lipo`, `codesign`, `security`, .NET 10 (cœur, infrastructure), xunit v3 + Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-24-macos-parity-design.md` (objectifs 5 et 6). Plan 1 sur 4 : le son du Mac, le surlignage sur Mac et les sous-titres sur Mac (avec le pas de 5 s du lecteur) ont chacun leur plan, écrit après celui-ci.

## Global Constraints

- Branche `feature/macos-app` ; chaque commit se termine par les lignes d'attribution de la session.
- **Aucun test en local sans le go explicite de l'utilisateur** (ils font planter une autre application sur son PC). Les étapes « Run » qui lancent `dotnet test` en local attendent ce go ; sans lui, la vérification passe par la CI : `git push origin feature/macos-app` puis `gh workflow run CI --ref feature/macos-app` et `gh run watch <id> --exit-status`. Un build local (`dotnet build`) n'est pas un test.
- L'app ne télécharge jamais rien : ffmpeg est récupéré par la CI au build, à une version épinglée vérifiée par SHA-256.
- ffmpeg épinglé : **9.0.2** sur les deux plateformes.
  - Windows : `https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip`, SHA-256 `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba` ; on garde `bin/ffmpeg.exe`.
  - macOS arm64 : `https://ffmpeg.martin-riedl.de/download/macos/arm64/1789931890_9.0.2/ffmpeg.zip`, SHA-256 `c8ed4c4e6978a03c485edbfe4e0a5dc2380f8a30bba5150531b31b094492d924`.
  - macOS x86_64 : `https://ffmpeg.martin-riedl.de/download/macos/amd64/1789931006_9.0.2/ffmpeg.zip`, SHA-256 `7c6b4125b191cbf773832dc51f424cf2b6bb7da43007d1e066f95909e47cacd4`.
  - Les deux binaires macOS exigent macOS 12 (compatible avec le minimum de l'app, 14) ; réunis par `lipo -create` en un binaire universel (~160 Mo).
  - Le serveur de M. Riedl refuse les requêtes sans User-Agent (403) : `curl` en envoie un par défaut, le garder.
- Licence : ffmpeg avec x264 est sous GPL v3 ; chaque zip contient `THIRD-PARTY-NOTICES.md` et `licenses/GPL-3.0.txt`, et le corps de chaque release renvoie au code source exact (`https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz`).
- Dans le bundle Mac, ffmpeg est `Contents/MacOS/ffmpeg`, signé avant l'app.
- Signature Mac : identité `ShowTime` d'un certificat auto-signé (usage « Code Signing »), importée depuis les secrets `MACOS_SIGNING_P12` (base64) et `MACOS_SIGNING_PASSWORD` ; sans ces secrets, signature ad hoc (`-`).
- Textes d'interface en français ; code, commentaires et scripts en anglais ; `dotnet format` avant chaque commit ; couverture du cœur ≥ 90 %.
- Un chemin ffmpeg configuré qui n'existe plus ne trouve rien (comportement voulu, figé par `A_configured_path_that_does_not_exist_finds_nothing_rather_than_another_ffmpeg`) : on n'y touche pas.

## Review Focus

1. **App Mac déplacée, renommée ou lancée depuis Téléchargements** (`/Users/x/Downloads/ShowTime 2.app`, translocation de Gatekeeper) : ffmpeg est trouvé dans le bundle. → `The_bundled_ffmpeg_is_found_wherever_the_app_is` (tâche 1).
2. **App Mac lancée hors bundle** (`dotnet run`, développement) : le dossier des assemblies reste le premier cherché, puis PATH et Homebrew. → `Outside_a_bundle_the_assemblies_folder_is_searched` (tâche 1).
3. **Zip Windows extrait à moitié** (ShowTime.exe copié seul) : le message dit d'extraire tout le zip, et ne parle plus de winget. → `The_install_hint_says_ffmpeg_comes_with_ShowTime` (tâche 1).
4. **Release ou CI sans les secrets de signature** (fork, PR externe) : le build passe, signé ad hoc. → étape de vérification de la tâche 4.
5. **Binaire épinglé corrompu ou remplacé chez le fournisseur** : le build échoue sur l'empreinte, rien n'est publié. → étape de vérification de la tâche 2.

---

### Task 1: le Mac cherche ffmpeg dans son bundle ; messages « livré avec ShowTime »

**Files:**
- Modify: `src/ShowTime.Infrastructure/Platform/AppPaths.cs` (`CreateFfmpegLocator`)
- Modify: `src/ShowTime.Core/Recording/AvFoundation.cs`, `src/ShowTime.Core/Recording/DirectShowGdigrab.cs` (`InstallHint`)
- Test: `tests/ShowTime.Infrastructure.IntegrationTests/Platform/AppPathsTests.cs`, `tests/ShowTime.Core.Tests/Recording/AvFoundationTests.cs`, `tests/ShowTime.Core.Tests/Recording/DirectShowGdigrabTests.cs`, `tests/ShowTime.Core.Tests/Recording/RecordingSessionTests.cs:96`

**Interfaces:**
- Produces: `public static string AppPaths.FfmpegDirectory(string baseDirectory)` — pour un dossier d'assemblies `…/X.app/Contents/MonoBundle/` (séparateurs `/`, barre finale ou non), renvoie `…/X.app/Contents/MacOS` ; sinon renvoie `baseDirectory` tel quel. `CreateFfmpegLocator` passe `FfmpegDirectory(AppContext.BaseDirectory)` comme dossier de l'app.

- [ ] **Step 1: Écrire les tests en échec**

```csharp
// AppPathsTests
[Theory]
[InlineData("/Applications/ShowTime.app/Contents/MonoBundle/", "/Applications/ShowTime.app/Contents/MacOS")]
[InlineData("/Users/moi/Downloads/ShowTime 2.app/Contents/MonoBundle", "/Users/moi/Downloads/ShowTime 2.app/Contents/MacOS")]
[InlineData("/private/var/folders/ab/T/AppTranslocation/1234/d/ShowTime.app/Contents/MonoBundle/", "/private/var/folders/ab/T/AppTranslocation/1234/d/ShowTime.app/Contents/MacOS")]
public void The_bundled_ffmpeg_is_found_wherever_the_app_is(string assemblies, string expected) =>
    AppPaths.FfmpegDirectory(assemblies).ShouldBe(expected);

[Theory]
[InlineData(@"C:\Program Files\ShowTime\")]
[InlineData("/Users/moi/ShowTime/src/ShowTime.Mac/bin/Debug/net10.0-macos/osx-arm64/")]
public void Outside_a_bundle_the_assemblies_folder_is_searched(string assemblies) =>
    AppPaths.FfmpegDirectory(assemblies).ShouldBe(assemblies);
```

`AvFoundationTests` : remplacer `The_install_hint_names_Homebrew` par `The_install_hint_says_ffmpeg_comes_with_ShowTime` — `InstallHint` contient « livré dans ShowTime.app » et ne contient ni « brew » ni « Homebrew ». `DirectShowGdigrabTests` : même test — contient « livré avec ShowTime » et « ffmpeg.exe », ne contient pas « winget ». `RecordingSessionTests:96` : l'avertissement contient « livré avec ShowTime » au lieu de « winget install Gyan.FFmpeg » (commentaire `// Deliberate change: ffmpeg now ships with ShowTime.`).

- [ ] **Step 2: Vérifier l'échec** — avec le go de l'utilisateur : `dotnet test --project tests/ShowTime.Core.Tests` et `dotnet test --project tests/ShowTime.Infrastructure.IntegrationTests` ; sinon par la CI (voir Global Constraints). Expected: échec de compilation (`FfmpegDirectory`) et des assertions de messages.

- [ ] **Step 3: Implémenter**
  - `FfmpegDirectory` : détecter le suffixe `.app/Contents/MonoBundle` (après retrait de la barre finale, comparaison ordinale) et remplacer `MonoBundle` par `MacOS`.
  - `AvFoundation.InstallHint` : « Il est livré dans ShowTime.app : réinstallez l'app depuis la page des versions, ou indiquez un autre ffmpeg dans les réglages. »
  - `DirectShowGdigrab.InstallHint` : « Il est livré avec ShowTime : extrayez de nouveau tout le zip, en gardant ffmpeg.exe à côté de ShowTime.exe, ou indiquez un autre ffmpeg dans les réglages. »
  - Doc XML de `FfmpegLocator` / `CreateFfmpegLocator` : « ffmpeg livré avec ShowTime (à côté de l'exe, ou dans Contents/MacOS), puis le PATH, puis Homebrew (développement) ».

- [ ] **Step 4: Vérifier le succès** — mêmes commandes (ou CI). Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -m "Look for the ffmpeg shipped with ShowTime, in the macOS bundle too"`

---

### Task 2: `build/fetch-ffmpeg.sh`, notices de licence, tests CI sur le ffmpeg livré

**Files:**
- Create: `build/fetch-ffmpeg.sh`, `THIRD-PARTY-NOTICES.md`, `licenses/GPL-3.0.txt` (texte officiel, `https://www.gnu.org/licenses/gpl-3.0.txt`)
- Modify: `.github/workflows/ci.yml` (jobs `windows` et `portable` : étape « Install ffmpeg »)

**Interfaces:**
- Produces: `build/fetch-ffmpeg.sh windows|macos <dest-dir>` — écrit `<dest-dir>/ffmpeg.exe` (windows) ou `<dest-dir>/ffmpeg` universel exécutable (macos), après vérification ; code de sortie non nul sinon. Les URL et empreintes de Global Constraints sont des variables en tête du script, avec la version (`FFMPEG_VERSION=9.0.2`).

- [ ] **Step 1: Écrire le script** — `set -euo pipefail` ; télécharger (`curl -sSfL`), vérifier l'empreinte (`sha256sum -c` ou `shasum -a 256 -c` selon l'OS, comme le modèle Whisper dans `ci.yml`), extraire (`unzip -j` du seul binaire), `lipo -create -output` pour macos, puis contrôler le binaire produit :
  - `ffmpeg -hide_banner -buildconf` contient `--enable-libx264` ;
  - `ffmpeg -hide_banner -devices` liste `avfoundation` (macos) ou `gdigrab` et `dshow` (windows) ;
  - `ffmpeg -version` commence par `ffmpeg version 9.0.2` (Gyan : `9.0.2-essentials_build-www.gyan.dev`) ;
  - macos : `lipo -archs` contient `arm64` et `x86_64`.

- [ ] **Step 2: `THIRD-PARTY-NOTICES.md`** — pour ffmpeg 9.0.2 : licence GPL v3 (avec x264), fournisseurs des binaires (gyan.dev, ffmpeg.martin-riedl.de), code source `https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz` ; pour whisper.cpp / Whisper.net et le modèle `ggml-small-q5_1.bin` : licence MIT et dépôt d'origine. En français pour les intitulés, une section par composant.

- [ ] **Step 3: Tests CI sur le binaire livré** — job `windows` : remplacer `choco install ffmpeg` par `bash build/fetch-ffmpeg.sh windows "$RUNNER_TEMP/ffmpeg"` puis ajout de ce dossier à `GITHUB_PATH` ; job `portable` : sous macOS, `bash build/fetch-ffmpeg.sh macos "$RUNNER_TEMP/ffmpeg"` + `GITHUB_PATH` au lieu de `brew install ffmpeg` ; Ubuntu garde `apt-get`.

- [ ] **Step 4: Vérifier** — `git push origin feature/macos-app`, `gh workflow run CI --ref feature/macos-app`, `gh run watch <id> --exit-status`. Expected: jobs `windows` et `portable (macos-latest)` verts, journal de l'étape montrant les contrôles du script ; les tests d'enregistrement ffmpeg (`FfmpegRecorderTests`, `SyncTests`…) passent avec ffmpeg 9.0.2. Empreinte fausse (Review Focus 5) : le script s'arrête sur l'échec de `sha256sum -c` grâce à `set -e`, à vérifier à la relecture du script.

- [ ] **Step 5: Commit** — `git commit -m "Fetch the pinned ffmpeg 9.0.2 in CI and test against it"`

---

### Task 3: zip Windows avec ffmpeg.exe et les notices

**Files:**
- Modify: `.github/workflows/ci.yml` (job `package-windows`), `.github/workflows/release.yml` (job `windows`, job `publish`)

**Interfaces:**
- Consumes: `build/fetch-ffmpeg.sh windows <dir>` (tâche 2), `THIRD-PARTY-NOTICES.md`, `licenses/` (tâche 2).

- [ ] **Step 1: Packaging** — après `dotnet publish … -o <dir>` : `bash build/fetch-ffmpeg.sh windows <dir>`, copie de `THIRD-PARTY-NOTICES.md` et `licenses/` dans `<dir>`. Dans `release.yml`, l'étape vit à côté de « Add the Whisper model ». Contrôle final : `test -f <dir>/ffmpeg.exe && test -f <dir>/ShowTime.exe && test -f <dir>/THIRD-PARTY-NOTICES.md`.
- [ ] **Step 2: Notes de release** — dans `publish`, `gh release create … --notes-file` : un fichier construit par l'étape avec la phrase « Contient ffmpeg 9.0.2 (GPL v3) ; code source : https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz. Voir THIRD-PARTY-NOTICES.md. », suivi des notes générées (`--generate-notes` reste).
- [ ] **Step 3: Vérifier** — CI dispatchée comme en tâche 2. Expected: job `package-windows` vert ; l'artefact `ShowTime-windows` contient `ShowTime.exe`, `ffmpeg.exe`, `runtimes\win-x64`, `THIRD-PARTY-NOTICES.md`, `licenses\GPL-3.0.txt` (`gh run download <id> -n ShowTime-windows -D <scratchpad>` puis lister ; ne rien exécuter).
- [ ] **Step 4: Commit** — `git commit -m "Ship ffmpeg.exe and the licence notices in the Windows zip"`

---

### Task 4: bundle Mac avec ffmpeg, signé avec le certificat stable

**Files:**
- Create: `build/package-macos.sh`
- Modify: `.github/workflows/ci.yml` (job `macos-app`), `.github/workflows/release.yml` (job `macos`)

**Interfaces:**
- Consumes: `build/fetch-ffmpeg.sh macos <dir>` (tâche 2).
- Produces: `build/package-macos.sh <ShowTime.app> <zip>` — lit les variables d'environnement optionnelles `MACOS_SIGNING_P12` et `MACOS_SIGNING_PASSWORD`.

- [ ] **Step 1: Écrire le script**, qui remplace l'étape « Sign ad hoc and zip » des deux workflows :
  1. `bash build/fetch-ffmpeg.sh macos <tmp>` puis copie en `"$app/Contents/MacOS/ffmpeg"` ; copie de `THIRD-PARTY-NOTICES.md` et `licenses/` dans `"$app/Contents/Resources/"`.
  2. Identité : si `MACOS_SIGNING_P12` est non vide, trousseau temporaire (`security create-keychain`, `set-keychain-settings -lut 21600`, `unlock-keychain`, `import <p12> -P "$MACOS_SIGNING_PASSWORD" -T /usr/bin/codesign`, `set-key-partition-list -S apple-tool:,apple:,codesign: -k <pw>`, ajout à `list-keychains`) et `identity=ShowTime` ; sinon `identity=-` et une ligne `echo "No signing certificate: signed ad hoc"`.
  3. `codesign --force --sign "$identity" "$app/Contents/MacOS/ffmpeg"` puis `codesign --force --deep --sign "$identity" "$app"` ; `codesign --verify --deep --strict --verbose=2 "$app"`.
  4. Contrôles : `lipo -archs` de `Contents/MacOS/ShowTime` et de `Contents/MacOS/ffmpeg` contiennent `arm64` et `x86_64` ; avec certificat, `codesign -dvv "$app" 2>&1` contient `Authority=LocalTake`.
  5. `ditto -c -k --keepParent "$app" "$zip"` ; trousseau temporaire supprimé dans un `trap … EXIT`.
- [ ] **Step 2: Workflows** — `macos-app` et `macos` appellent `bash build/package-macos.sh "$app" dist/<nom>.zip` avec `env: MACOS_SIGNING_P12: ${{ secrets.MACOS_SIGNING_P12 }}` et `MACOS_SIGNING_PASSWORD: ${{ secrets.MACOS_SIGNING_PASSWORD }}`. Le commentaire de `macos-app` passe de « signed ad hoc » à « signed with the LocalTake certificate when the secrets are set, ad hoc otherwise ».
- [ ] **Step 3: Vérifier sans secret** (ils n'existent pas encore) — CI dispatchée. Expected: `macos-app` vert, journal « No signing certificate: signed ad hoc », artefact `ShowTime-macos` contenant `ShowTime.app/Contents/MacOS/ffmpeg` (télécharger et lister avec `unzip -l`, sans exécuter).
- [ ] **Step 4: Commit** — `git commit -m "Ship ffmpeg in the macOS app and sign it with a stable certificate when available"`

---

### Task 5: certificat de signature (action de l'utilisateur) et documentation

**Files:**
- Create: `docs/macos-signing.md`
- Modify: `README.md` (Installation, ffmpeg, Fichiers), `CLAUDE.md` (Project, Commands, Video, Versioning and releases), `docs/test-checklist.md`

- [ ] **Step 1: `docs/macos-signing.md`** — pourquoi (autorisations rattachées à la signature), puis les commandes que l'utilisateur lance lui-même (préfixe `!` dans la session), depuis Git Bash, dans un dossier temporaire :
  - `openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=LocalTake" -addext "keyUsage=critical,digitalSignature" -addext "extendedKeyUsage=critical,codeSigning" -keyout localtake.key -out localtake.crt`
  - `openssl pkcs12 -export -legacy -inkey localtake.key -in localtake.crt -name LocalTake -out localtake.p12` (mot de passe demandé ; `-legacy` car `security import` refuse les chiffrements par défaut d'OpenSSL 3)
  - `base64 -w0 localtake.p12 | gh secret set MACOS_SIGNING_P12` et `gh secret set MACOS_SIGNING_PASSWORD`
  - garder `localtake.p12` et son mot de passe en lieu sûr (le perdre change l'identité : une réautorisation de plus), puis effacer les fichiers du dossier temporaire.
- [ ] **Step 2: Pause — l'utilisateur crée le certificat et les deux secrets.** Ne rien générer à sa place (la clé privée ne doit pas passer par la session).
- [ ] **Step 3: Vérifier avec secret** — CI dispatchée. Expected: `macos-app` vert, journal contenant `Authority=LocalTake`.
- [ ] **Step 4: Documentation**
  - README : Windows « extraire le zip, lancer ShowTime.exe ; ffmpeg est inclus » ; macOS « glisser dans Applications ; ffmpeg est inclus ; première ouverture par clic droit > Ouvrir ; les mises à jour gardent les autorisations » ; supprimer `winget install Gyan.FFmpeg` et `brew install ffmpeg` (BlackHole reste jusqu'au plan du son) ; mention de `THIRD-PARTY-NOTICES.md`.
  - CLAUDE.md : « ffmpeg ships with the app, pinned and checked in `build/fetch-ffmpeg.sh` (the app never downloads it) » ; recherche de ffmpeg (réglage, puis livré : à côté de l'exe ou `Contents/MacOS`, puis PATH, puis Homebrew) ; release : zips avec ffmpeg et notices, signature Mac par `build/package-macos.sh` et `docs/macos-signing.md`.
  - `docs/test-checklist.md` : Windows — sur un PC sans ffmpeg, le zip extrait enregistre une vidéo ; macOS — sur un Mac sans Homebrew, l'app enregistre ; après installation d'une nouvelle version signée, l'Enregistrement de l'écran et le Micro n'ont pas à être redonnés.
- [ ] **Step 5: Commit** — `git commit -m "Document the macOS signing certificate and the self-contained install"`
