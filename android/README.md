# IPTV Player — Android / Android TV

Une seule application (Kotlin + Jetpack Compose + libVLC) pour téléphone, tablette et Android TV.
Plan complet : [`../docs/PLAN-multiplateforme.md`](../docs/PLAN-multiplateforme.md) · protocole de contrôle : [`../docs/protocole-controle-v1.md`](../docs/protocole-controle-v1.md).

## Prérequis (installés une fois)

* JDK 17 dans `%USERPROFILE%\jdk-17` et SDK Android dans `%USERPROFILE%\Android\Sdk`
  (plateforme 36, build-tools 37.0.0, émulateur, images système Android TV et téléphone x86_64).
* Appareils virtuels : `iptv_phone` (Pixel 6) et `iptv_tv` (Android TV 1080p).

## Commandes (PowerShell, depuis la racine du dépôt)

```powershell
. .\tools\android-env.ps1                 # variables d'environnement + local.properties
cd android
.\gradlew.bat assembleDebug               # compile (un APK par architecture ; x86_64 pour l'émulateur) -> app\build\outputs\apk\debug\app-x86_64-debug.apk

emulator -avd iptv_phone                  # ou : emulator -avd iptv_tv
adb install -r app\build\outputs\apk\debug\app-x86_64-debug.apk
adb shell am start -n app.iptvplayer/.MainActivity
```

## Structure

| Dossier | Contenu |
|---|---|
| `app/src/main/java/app/iptvplayer/` | Code Kotlin (`MainActivity`, `ui/`) |
| `app/src/main/res/values/` | Textes anglais (**langue par défaut**) ; `values-fr/` pour le français |
| `gradle/libs.versions.toml` | Versions de toutes les dépendances (un seul endroit) |

Les icônes sont générées par `tools\make-android-icons.ps1` depuis le dessin du logo.

## Tester sans compte réel (faux serveur Xtream)

```powershell
python tools\mock-xtream\server.py        # http://0.0.0.0:8099 — identifiants : demo / demo
# dans l'application (émulateur) : serveur http://10.0.2.2:8099, utilisateur demo, mot de passe demo
```

Le catalogue fictif contient 60 chaînes, 45 films et 12 séries (2 saisons de 4 épisodes) ; tous les flux pointent vers un flux HLS de test.

## Tests unitaires

```powershell
cd android; .\gradlew.bat testDebugUnitTest      # analyseurs Xtream / M3U
```

## État fonctionnel (jalons M2 téléphone + M3 TV)

Comptes Xtream et M3U (chiffrés avec le Keystore) · catalogue en cache · Live / Films / Séries · recherche · favoris ·
lecture libVLC plein écran (reprise, pistes audio et sous-titres, langue préférée) · historique avec reprise automatique ·
épisodes vus / entamés · langue de l'interface EN (défaut) / FR à chaud.

Télé : interface à barre latérale, focus visible à la télécommande ; dans le lecteur OK = pause, ←/→ = ±10 s (films), ↑/↓ = chaîne suivante/précédente (direct). Guide : émission en cours affichée dans les listes Live (XMLTV, 6 h–+36 h).

Contrôle à distance (cible) : Réglages > Contrôle à distance (désactivé par défaut). Serveur HTTPS sur le port 47231, annonce mDNS `_iptvplayer._tcp`, appairage par PIN affiché à l'écran. Protocole : `../docs/protocole-controle-v1.md`. Preuve du PIN : HMAC-SHA256 sur `label
pairingId
clientNonce
serverNonce
empreinte` (labels `iptvplayer-pair-v1` / `iptvplayer-pair-v1-server`).

## Version de distribution (release)

```powershell
cd android; .\gradlew.bat assembleRelease          # appuild\outputspk
eleasepp-release-unsigned.apk (3 architectures, ~170 Mo)
```

Pour signer : créer une clé (`keytool -genkeypair -v -keystore iptv-release.jks -alias iptv -keyalg RSA -keysize 4096 -validity 10000`)
puis un fichier `android/keystore.properties` **non versionné** (déjà dans `.gitignore`) contenant `storeFile`, `storePassword`, `keyAlias`, `keyPassword` ;
`assembleRelease` produit alors un APK signé. Garder la clé et ses mots de passe en lieu sûr : sans elle, impossible de publier des mises à jour.

Licences : `app/src/main/assets/THIRD_PARTY_NOTICES.md` (affiché dans Réglages ▸ « Licences des composants »).
**À lire avant toute diffusion publique** : l'audit a relevé des symboles de l'encodeur x264 (GPL) dans `libvlc.so` ; voir l'avertissement dans ce fichier.

La cible de contrôle à distance n'écoute que tant que l'application est ouverte : la lecture a besoin de l'écran, et Android interdit de lancer une activité depuis l'arrière-plan ; un service d'arrière-plan n'apporterait donc rien d'utile pour l'instant.

## Moteur libVLC en LGPL

Le paquet officiel de VideoLAN est compilé en GPLv3 (x264 inclus). Pour une diffusion, utiliser la version recompilée en LGPL :

```bash
# sous Linux / WSL (Ubuntu 24.04), ~30 min par architecture, plusieurs Go de téléchargements (NDK, sources VLC)
tools/build-libvlc-lgpl.sh arm64 x86_64
```

Les bibliothèques sont copiées dans `android/libvlc-lgpl/jniLibs/<abi>/` (ignoré par git) ; `gradlew assembleDebug|assembleRelease` les utilise automatiquement.
Sans ce dossier, le build retombe sur le paquet officiel (usage privé uniquement).

## Configurer un nouvel appareil (QR code)

* **Téléphone / tablette, premier lancement** : « Se connecter avec un autre appareil » → scanner le QR code affiché par l'application Windows (Réglages ▸ « Partager ma configuration ») ou par un autre appareil Android (Réglages ▸ « Afficher un QR code… »), ou saisir `ip:port` et le code.
* **TV, premier lancement** : l'écran « Configurer cette TV » affiche un QR code ; sur le téléphone : Réglages ▸ Télécommande ▸ Scanner un QR code.

