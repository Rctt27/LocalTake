# Signer l'app macOS avec un certificat stable

## Pourquoi

macOS rattache les autorisations **Enregistrement de l'écran** et **Micro** à la signature de l'app. Signée « ad hoc »,
LocalTake change de signature à chaque version : macOS redemanderait ces autorisations après chaque mise à jour, ou
afficherait LocalTake comme autorisée alors que la capture échoue.

Signée avec un certificat qui ne change pas, l'app garde son identité d'une version à l'autre, et macOS garde les
autorisations. Le certificat est auto-signé : gratuit, sans compte Apple. Il ne remplace pas la notarisation : la toute
première ouverture se fait toujours par **clic droit > Ouvrir** (ou Réglages Système > Confidentialité et sécurité >
« Ouvrir quand même »).

## Créer le certificat (une seule fois)

Dans **Git Bash** sous Windows, dans un dossier temporaire. (Sur un Mac, l'`openssl` d'Apple est LibreSSL, qui ne connaît pas `-legacy`, et `base64` n'a pas `-w` : utilisez l'`openssl` de Homebrew et `base64 -i localtake.p12`.) La clé privée reste sur votre machine et dans les
secrets GitHub : ne la donnez à personne, et ne la commitez jamais.

Une commande par ligne (Git Bash coupe mal les longues lignes collées). `MSYS_NO_PATHCONV=1` empêche Git Bash de
prendre `/CN=LocalTake` pour un chemin, et le mot de passe se tape dans `read -s` (Git Bash n'affiche pas toujours
l'invite d'openssl) :

```bash
MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=LocalTake"   -addext "keyUsage=critical,digitalSignature" -addext "extendedKeyUsage=critical,codeSigning"   -keyout localtake.key -out localtake.crt
```

```bash
read -s -p "Mot de passe du certificat : " P; echo
```

```bash
P="$P" openssl pkcs12 -export -legacy -inkey localtake.key -in localtake.crt -name LocalTake -out localtake.p12 -passout env:P
```

```bash
P="$P" openssl pkcs12 -in localtake.p12 -legacy -passin env:P -noout && echo OK
```

`-legacy` est nécessaire : l'outil `security` de macOS refuse les chiffrements qu'OpenSSL 3 utilise par défaut.
Puis, depuis le dossier du dépôt LocalTake, dans la même fenêtre :

```bash
base64 -w0 /chemin/vers/localtake.p12 | gh secret set MACOS_SIGNING_P12
```

```bash
printf '%s' "$P" | gh secret set MACOS_SIGNING_PASSWORD; unset P
```

Gardez `localtake.p12` et son mot de passe en lieu sûr (un gestionnaire de mots de passe), puis effacez les trois
fichiers du dossier temporaire. Perdre le certificat n'empêche rien, mais un nouveau certificat est une nouvelle
identité : chaque utilisateur redonnera une fois les autorisations.

## Ce que fait la CI

`build/package-macos.sh` (jobs `macos-app` de la CI et `macos` de la release) importe le certificat dans un trousseau
temporaire, signe `Contents/MacOS/ffmpeg` puis l'app avec l'identité `LocalTake`, et vérifie que la signature porte
`Authority=LocalTake`. Sans les deux secrets (un fork, une pull request externe), la CI signe ad hoc et l'affiche en
avertissement ; la release, elle, s'arrête (`REQUIRE_SIGNING=1`) plutôt que de publier une version signée ad hoc.

Le certificat est valable 10 ans. Pour le renouveler, recommencer la procédure ci-dessus : les utilisateurs redonneront
les autorisations une fois.
