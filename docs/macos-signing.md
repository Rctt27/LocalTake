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

Dans Git Bash (ou le Terminal d'un Mac), dans un dossier temporaire. La clé privée reste sur votre machine et dans les
secrets GitHub : ne la donnez à personne, et ne la commitez jamais.

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=LocalTake" \
  -addext "keyUsage=critical,digitalSignature" -addext "extendedKeyUsage=critical,codeSigning" \
  -keyout localtake.key -out localtake.crt
openssl pkcs12 -export -legacy -inkey localtake.key -in localtake.crt -name LocalTake -out localtake.p12
```

La seconde commande demande un mot de passe pour le fichier `.p12`. L'option `-legacy` est nécessaire : l'outil
`security` de macOS refuse les chiffrements qu'OpenSSL 3 utilise par défaut.

Puis, depuis le dossier du dépôt LocalTake (le second `gh` demande le mot de passe du `.p12`) :

```bash
base64 -w0 /chemin/vers/localtake.p12 | gh secret set MACOS_SIGNING_P12
gh secret set MACOS_SIGNING_PASSWORD
```

Gardez `localtake.p12` et son mot de passe en lieu sûr (un gestionnaire de mots de passe), puis effacez les trois
fichiers du dossier temporaire. Perdre le certificat n'empêche rien, mais un nouveau certificat est une nouvelle
identité : chaque utilisateur redonnera une fois les autorisations.

## Ce que fait la CI

`build/package-macos.sh` (jobs `macos-app` de la CI et `macos` de la release) importe le certificat dans un trousseau
temporaire, signe `Contents/MacOS/ffmpeg` puis l'app avec l'identité `LocalTake`, et vérifie que la signature porte
`Authority=LocalTake`. Sans les deux secrets (un fork, une pull request externe), il signe ad hoc et l'écrit dans le
journal.

Le certificat est valable 10 ans. Pour le renouveler, recommencer la procédure ci-dessus : les utilisateurs redonneront
les autorisations une fois.
