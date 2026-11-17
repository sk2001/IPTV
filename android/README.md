# IPTV Player — Android / Android TV

Une seule application (Kotlin + Jetpack Compose + libVLC) pour téléphone, tablette et Android TV.
Plan complet : [`../docs/PLAN-multiplateforme.md`](../docs/PLAN-multiplateforme.md) · protocole de contrôle : [`../docs/protocole-controle-v1.md`](../docs/protocole-controle-v1.md).

Toutes les commandes se lancent dans **PowerShell, depuis la racine du dépôt**, sauf mention contraire.

## 1. Prérequis (une seule fois)

| Outil | Où | Remarque |
|---|---|---|
| JDK 17 | `%USERPROFILE%\jdk-17` | Temurin 17 (zip, sans installation) |
| SDK Android | `%USERPROFILE%\Android\Sdk` | plateforme 36, build-tools 37.0.0, platform-tools (`adb`), émulateur et images système si besoin |
| Python 3 | dans le `PATH` | seulement pour le faux serveur de test |
| WSL Ubuntu 24.04 | — | seulement pour recompiler libVLC en LGPL (§5) |

Gradle (9.9.1) est téléchargé automatiquement par `gradlew`. Si les outils sont ailleurs, adapter `tools\android-env.ps1`.

À chaque nouvelle fenêtre PowerShell :

```powershell
. .\tools\android-env.ps1        # JAVA_HOME, ANDROID_HOME, PATH + android\local.properties (le point initial est voulu)
cd android
```

> **Raccourci depuis la racine du dépôt** : `./build.ps1 android` compile et copie les APK dans `dist\android\` (debug, ou release si `android\keystore.properties` existe ; option `-AndroidVariant debug|release`). Les sections suivantes détaillent ce que le script fait.

## 2. Compiler un APK de test (debug)

```powershell
.\gradlew.bat assembleDebug
```

Résultats dans `android\app\build\outputs\apk\debug\` : un APK par architecture.

| Fichier | Pour |
|---|---|
| `app-arm64-v8a-debug.apk` | **téléphones et TV récents (presque tous)** |
| `app-x86_64-debug.apk` | émulateur Android sur PC |
| `app-universal-debug.apk` | toutes les architectures compilées (plus gros) |

Il est signé automatiquement avec la clé de débogage Android : aucune clé à créer.

## 3. Installer

### Sur un téléphone ou une TV réels

1. Activer les **options pour les développeurs** (Paramètres ▸ À propos ▸ toucher 7 fois « Numéro de build »), puis le **débogage USB** (sur TV : débogage réseau, `adb connect <ip-de-la-tv>`).
2. Brancher l'appareil et accepter l'autorisation USB à l'écran.
3. Installer :

```powershell
adb devices                                                            # l'appareil doit apparaître « device »
adb install -r app\build\outputs\apk\debug\app-arm64-v8a-debug.apk
adb shell am start -n app.iptvplayer/.MainActivity                     # lancement (ou ouvrir l'icône)
```

Sans câble : copier l'APK sur l'appareil (USB, cloud…), l'ouvrir et autoriser l'installation depuis cette source.

> Si une version signée **autrement** est déjà installée (debug ↔ release, autre clé), la désinstaller d'abord : `adb uninstall app.iptvplayer`.
> Attention : désinstaller efface aussi les données de l'appli et son certificat de contrôle à distance ; il faudra ré-appairer l'appareil (le PC affichera « le certificat a changé »).

### Sur l'émulateur

```powershell
emulator -avd iptv_phone                   # ou : emulator -avd iptv_tv  (Android TV 1080p)
adb install -r app\build\outputs\apk\debug\app-x86_64-debug.apk
adb shell am start -n app.iptvplayer/.MainActivity
```

Créer les appareils virtuels si besoin : `avdmanager create avd -n iptv_phone -k "system-images;android-36;google_apis;x86_64" -d pixel_6`.

## 4. Compiler une version de distribution (release)

Un APK release **doit être signé** pour s'installer.

**a) Créer la clé (une seule fois)** — garder le fichier et le mot de passe en lieu sûr : sans eux, plus de mises à jour possibles.

```powershell
& "$env:USERPROFILE\jdk-17\bin\keytool.exe" -genkeypair -v -keystore android\iptv-release.jks -alias iptv -keyalg RSA -keysize 4096 -validity 10000
```

**b) Créer `android\keystore.properties`** (non versionné, ignoré par git) :

```
storeFile=iptv-release.jks
storePassword=VOTRE_MOT_DE_PASSE
keyAlias=iptv
keyPassword=VOTRE_MOT_DE_PASSE
```

**c) Compiler et installer**

```powershell
.\gradlew.bat assembleRelease
adb install -r app\build\outputs\apk\release\app-arm64-v8a-release.apk
```

Sans l'étape b, on obtient `app-arm64-v8a-release-unsigned.apk`, que l'appareil refuse.

## 5. Moteur libVLC en LGPL (obligatoire avant toute diffusion)

Le paquet officiel de VideoLAN est compilé en **GPLv3** (il contient l'encodeur x264). Pour distribuer l'APK, utiliser la version recompilée en **LGPL** :

```bash
# sous WSL / Linux (Ubuntu 24.04) ; ~30 min par architecture ; plusieurs Go de téléchargements (NDK r27, sources VLC)
tools/build-libvlc-lgpl.sh arm64 x86_64
```

Les bibliothèques sont copiées dans `android/libvlc-lgpl/jniLibs/<abi>/` (ignoré par git) et **utilisées automatiquement** par `assembleDebug` / `assembleRelease` ; seules les architectures présentes dans ce dossier sont compilées.
Sans ce dossier, le build retombe sur le paquet officiel : **usage privé uniquement** (voir `app/src/main/assets/THIRD_PARTY_NOTICES.md`, aussi affiché dans Réglages ▸ « Licences des composants »).
Depuis WSL, le dépôt est visible sous `/mnt/c/Users/<vous>/git/IPTV`.

## 6. Tests

```powershell
.\gradlew.bat testDebugUnitTest            # analyseurs Xtream / M3U / XMLTV, protocole de contrôle, appairage, synchronisation, QR
```

Faux serveur Xtream pour essayer sans compte réel :

```powershell
python tools\mock-xtream\server.py         # http://0.0.0.0:8099 — identifiants : demo / demo
# dans l'application (émulateur) : serveur http://10.0.2.2:8099 ; sur un vrai téléphone : http://<ip-du-PC>:8099
```

Il simule 60 chaînes, 45 films et 12 séries ; les flux pointent vers un flux HLS de test public.

## 7. Structure

| Dossier | Contenu |
|---|---|
| `app/src/main/java/app/iptvplayer/` | Code Kotlin : `ui/` (téléphone et TV), `remote/` (contrôle à distance), `sync/`, `data/`, `net/`, `player/` |
| `app/src/main/res/values/` | Textes anglais (**langue par défaut**) ; `values-fr/` pour le français |
| `libvlc-lgpl/` | Sources Java de libVLC (libvlcjni) et emplacement des bibliothèques natives LGPL |
| `gradle/libs.versions.toml` | Versions de toutes les dépendances (un seul endroit) |

Les icônes sont générées par `tools\make-android-icons.ps1` depuis le dessin du logo.

## 8. Fonctionnalités

Comptes Xtream et M3U (chiffrés avec le Keystore) · catalogue en cache · Live / Films / Séries · recherche · favoris · lecture libVLC plein écran (reprise, pistes audio et sous-titres, langue préférée) · historique avec reprise automatique · épisodes vus / entamés · émission en cours (XMLTV) · langue EN (défaut) / FR à chaud.

**TV** : barre latérale, focus visible à la télécommande ; dans le lecteur, OK = pause, ←/→ = ±10 s (films), ↑/↓ = chaîne suivante / précédente (direct).

**Contrôle à distance** (Réglages ▸ Contrôle à distance, désactivé par défaut) : serveur HTTPS (port 47231) à certificat épinglé, annonce mDNS `_iptvplayer._tcp`, appairage par PIN ou QR code. L'appareil peut être piloté depuis un autre appareil Android ou depuis l'application Windows ; « Envoyer vers… » dans le lecteur transmet la lecture en cours. La cible n'écoute que tant que l'application est ouverte.

**Synchronisation** favoris / historique / réglages (et comptes, en option des deux côtés), à la demande.

**Configurer un nouvel appareil** : au premier lancement, « Se connecter avec un autre appareil » → scanner le QR code affiché par l'application Windows (Réglages ▸ « Partager ma configuration… ») ou par un autre appareil Android (Réglages ▸ « Afficher un QR code… »), ou saisir `ip:port` et le code. Sur **TV**, l'écran « Configurer cette TV » affiche un QR code à scanner depuis le téléphone (Réglages ▸ Télécommande ▸ Scanner un QR code).
Le téléphone et l'autre appareil doivent être sur le même réseau local.

## TV en direct « à la Plex » (téléphone, tablette, TV)

L'onglet **Live** s'ouvre sur le guide :

* **Bandeau** : chaîne, « À L'ANTENNE » / « À SUIVRE », titre, horaire, progression, description (XMLTV), boutons *Regarder*, favori et *Dernière chaîne*.
* **Guide en grille** : chaînes en lignes, programmes en colonnes horaires (de −2 h à +36 h), ligne « maintenant » ; défilement horizontal commun à toutes les lignes ; les chaînes sans programme restent listées. Téléphone : toucher un programme le met en avant, le toucher à nouveau lance la chaîne. TV : le focus d'un programme met le bandeau à jour, OK lance la chaîne, appui long sur une chaîne = favori.
* **Mini-lecteur** : Retour dans le lecteur plein écran ramène au guide et la lecture continue — dans le bandeau (TV, tablette) ou dans une barre en bas (téléphone) ; toucher / OK = plein écran. Changer d'onglet arrête le mini-lecteur.
* Le faux serveur de test (`tools/mock-xtream/server.py`) fournit un guide fictif pour les 60 chaînes.

