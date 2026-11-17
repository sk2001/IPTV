# Plan multiplateforme : Android, Android TV et contrôle à distance

> Statut : **brouillon validé sur les grandes lignes** (2026-11-16). Document vivant : à mettre à jour à chaque jalon.

## 1. Décisions prises

| Sujet | Décision | Pourquoi |
|---|---|---|
| Technologie Android | **Kotlin + Jetpack Compose + libVLC** | Natif, meilleur support Android TV (navigation à la télécommande), même moteur VLC que le PC (comportement identique sur les flux IPTV) |
| Formats | **Une seule application** adaptative téléphone / tablette / Android TV | Un seul code et un seul APK ; l'interface s'adapte (tactile ou télécommande) |
| Contrôle réseau | **Télécommande de lecture** + **envoi d'une lecture** + **synchronisation** (comptes, favoris, historique) | Demande de l'utilisateur ; la saisie à distance (clavier du téléphone) est reportée |
| **Le PC n'est pas piloté** | L'application Windows ne reçoit aucune commande : elle est **cliente seulement** (elle peut piloter le téléphone/la TV et synchroniser en les contactant elle-même) | Décision de l'utilisateur (2026-11-16) : pas de serveur sur le PC, donc pas de pare-feu à ouvrir ni de surface d'attaque côté PC |
| Outils | Installés par l'assistant (JDK 17 + SDK Android en ligne de commande, sans droits admin) | Rien n'était installé ; chaque gros téléchargement est confirmé |
| Licence | MIT pour le code du projet ; libVLC Android en LGPL (audit à refaire, voir §8) | Cohérent avec la version Windows |

## 2. Vue d'ensemble

```
                          réseau local (Wi-Fi / Ethernet)
   ┌────────────────┐        mDNS _iptvplayer._tcp        ┌────────────────┐
   │  PC Windows    │────────── client seulement ─────────►│  Android TV    │
   │  (WPF, C#)     │      HTTPS + WebSocket (API v1)     │  (Kotlin)      │
   │  n'écoute pas  │                                      │  serveur ctrl. │
   └────────────────┘                                      └───────▲────────┘
           │                  ┌────────────────┐                   │
           └─────── client ──►│  Téléphone     ├───────────────────┘
                              │  (Kotlin)      │   télécommande, « envoyer vers », synchro
                              │  serveur ctrl. │
                              └────────────────┘
```

* **Les appareils Android (téléphone, tablette, TV) peuvent être cible** (ils lisent la vidéo et exposent l'API) **et contrôleur**. **Le PC est contrôleur seulement.**
* Le téléphone lit aussi lui-même (mode autonome) ; la TV s'utilise seule à la télécommande.
* Aucun serveur Internet : tout reste sur le réseau local, **désactivé par défaut**, appairage par code PIN.

## 3. Protocole de contrôle

Spécification détaillée : [`docs/protocole-controle-v1.md`](protocole-controle-v1.md).
Résumé : découverte **mDNS**, **HTTPS** à certificat auto-signé épinglé à l'appairage (PIN à 6 chiffres affiché sur l'appareil cible),
jeton par appareil, état en **WebSocket**, commandes en JSON, synchronisation pull/push avec résolution « dernière écriture gagne ».

## 4. Application Android (`android/`)

Un seul module `app` au début, découpé en packages (extraction en modules Gradle ensuite si besoin) :

| Package | Rôle |
|---|---|
| `model` | Compte, élément (live/film/série), catégorie, épisode, EPG, historique |
| `net` | Clients **Xtream** et **M3U**, analyseur **XMLTV** (équivalents Kotlin de `XtreamClient`, `M3uParser`, `EpgService`) |
| `data` | Cache catalogue (Room + recherche plein texte), comptes (chiffrés avec Android Keystore), favoris, historique, préférences (DataStore) |
| `player` | Enveloppe libVLC (lecture, pistes audio/sous-titres, reprise, enregistrement `.ts`), `MediaSession` |
| `remote` | Serveur de contrôle (Ktor), client, découverte NSD, appairage, synchronisation |
| `ui` | Compose : thème commun, écrans communs, variantes `phone` et `tv` (Compose pour TV, focus D-pad) |
| `i18n` | Ressources `values/` (anglais, **défaut**) et `values-fr/` |

Bibliothèques envisagées : Kotlin 2.x, Compose BOM + `androidx.tv:tv-material`, Navigation, Coroutines/Flow, kotlinx.serialization,
Ktor (client + serveur, Apache-2.0), Room, Coil (images, Apache-2.0), `org.videolan.android:libvlc-all` (LGPL).
`minSdk` 24 (Android 7), `targetSdk`/`compileSdk` 36.

Détection de la TV : `UiModeManager` = `UI_MODE_TYPE_TELEVISION`. Manifeste : `LEANBACK_LAUNCHER`, bannière 320×180,
`android.hardware.touchscreen` et `android.software.leanback` **non requis**.

Parité fonctionnelle visée avec Windows : comptes Xtream/M3U, Live/Films/Séries, recherche, favoris, guide TV, historique + reprise,
langue audio/sous-titres préférée, langue de l'interface (EN par défaut / FR), enregistrement (ensuite).

## 5. Côté PC (Windows) — client seulement

Le PC **n'écoute rien** : pas de serveur, pas de règle de pare-feu, exe inchangé. Il contacte les appareils Android qui exposent l'API :

* **Découverte** : recherche mDNS `_iptvplayer._tcp` (bibliothèque cliente MIT) + saisie manuelle de l'adresse.
* **Appairage** : le PC saisit le PIN affiché par le téléphone / la TV ; le jeton est stocké chiffré (DPAPI) avec l'empreinte du certificat.
* **Télécommande** : panneau « Appareils » (liste des appareils appairés, état de lecture, boutons de contrôle) et action « Envoyer vers… » sur un contenu.
* **Synchronisation** : c'est le PC qui initie (tirer puis envoyer) vers un appareil appairé ; comptes, favoris, historique.
* Paramètres → **Appareils** : appairer, oublier, option « Synchroniser les comptes ».

## 6. Jalons

| # | Jalon | Livrable vérifiable |
|---|---|---|
| M0 | **Outils + squelette** ✅ *(2026-11-16)* | JDK/SDK installés, projet Gradle `android/` qui compile et se lance sur émulateur (téléphone **et** TV) : fait, libVLC 3.0.25 chargé sur les deux |
| M1 | **Client de contrôle côté PC** ✅ *(2026-11-17, sans synchro)* | **Fait** : `Services/Remote/` (client HTTPS à empreinte épinglée, appairage PIN, jetons DPAPI, découverte mDNS par interrogation ponctuelle), fenêtre « Télécommande » (Réglages) et bouton « Envoyer vers un autre appareil » dans le lecteur ; le PC n'écoute aucun port. Client C# testé contre l'appli Android sur émulateur (appairage, état, volume, envoi de lecture, empreinte fausse refusée) ; **mDNS côté PC non testé** (pas de réseau commun avec l'émulateur) ; l'interface WPF n'a pas été pilotée à la main. **Reste** : synchro (M5). Objectif initial : le PC découvre, appaire (PIN), pilote et synchronise un appareil Android ; testé contre l'application Android (M4) ou un faux serveur de test |
| M2 | **Android cœur** ✅ *(2026-11-16, interface téléphone)* | Comptes Xtream/M3U chiffrés (Keystore), catalogue en cache, Live/Films/Séries, recherche, favoris, lecture libVLC (reprise, pistes audio/sous-titres), historique, langues EN/FR : testé de bout en bout sur l'émulateur contre un faux serveur Xtream. **Reste** : guide TV (EPG), enregistrement, test avec un vrai compte |
| M3 | **Interface TV** ✅ *(2026-11-16)* | Détection TV (`UiModeManager`), barre latérale + catégories + liste/grille d'affiches à focus D-pad (liseré blanc), appui long = favori / retrait de l'historique, lecteur piloté à la télécommande (OK = pause, ←/→ = ±10 s, ↑/↓ ou CH+/CH− = zapping en direct), émission en cours (EPG XMLTV, analyseur SAX testé) dans les listes. Testé sur l'émulateur TV. **Reste** : grille complète du guide, enregistrement |
| M4 | **Télécommande réseau Android** ✅ *(2026-11-17, sans WebSocket testé ni service d'arrière-plan)* | **Fait côté cible** : serveur HTTPS (certificat ECDSA dans le Keystore, TLS 1.3, port 47231), annonce NSD, appairage PIN lié à l'empreinte, jetons hachés, limites de débit, commandes (play, pause, seek, volume, muet, pistes, zapping, stop), état diffusé en WebSocket, Réglages « Contrôle à distance » (PIN, appareils, révocation) ; 9 tests unitaires + test réel sur émulateur (appairage, envoi de lecture, pause, volume). **Contrôleur Android fait** (2026-11-17) : écran « Télécommande » (Réglages), appairage avec PIN et épinglage de l'empreinte, saisie manuelle de l'adresse, transport/volume/pistes, bouton « Envoyer vers… » dans le lecteur ; état suivi par interrogation de `/state` (1 s) ; testé sur deux émulateurs (téléphone vers TV : appairage, envoi de lecture) ; la découverte mDNS entre appareils n'est pas testable sur émulateurs (réseaux séparés). **Reste** : QR code, WebSocket côté client, service d'arrière-plan (la cible n'écoute que tant que l'appli vit), `playRef` pour les épisodes |
| M5 | **Synchronisation** ✅ *(2026-11-17)* | Favoris, historique, préférences de lecture et (option, des deux côtés) comptes ; dernière écriture gagnée, suppressions conservées 90 jours, changements locaux détectés à la synchro par empreinte (moteur identique en Kotlin `sync/SyncEngine.kt` et en C# `Services/Remote/SyncEngine.cs`) ; routes `/sync/manifest`, `GET|PUT /sync/{collection}` côté Android ; boutons « Synchroniser » (Android contrôleur et PC). 6 tests unitaires du moteur ; client C# testé contre l'appli sur émulateur (échange dans les deux sens, 2e synchro sans changement, comptes refusés si désactivé). **Limites** : horodatage des favoris/réglages = moment de la détection à la synchro ; synchro à la demande (pas automatique) ; le test de bout en bout de l'interface WPF et de la synchro des comptes/historique réels n'a pas été fait |
| M6 | **Finition** ◐ *(2026-11-17)* | **Fait** : audit des licences Android (`THIRD_PARTY_NOTICES.md` + LGPL-2.1 affichés dans Réglages), build `assembleRelease` vérifié (APK non signé ~170 Mo), signature par `keystore.properties` non versionné. **Point bloquant avant diffusion publique** : symboles x264 (GPL-2+) trouvés dans `libvlc.so` malgré la licence LGPL déclarée par le paquet — à faire confirmer par VideoLAN ou à reconstruire sans GPL. **Fait aussi** : un APK par architecture (arm64 ~70 Mo). **Décision** : pas de service d'arrière-plan pour la cible (la lecture exige l'écran et Android bloque le lancement d'activité depuis l'arrière-plan). **QR code d'appairage fait** (affiché par la cible dans Réglages, lu par la caméra du contrôleur Android ; le PC colle le lien) : génération/lecture non testées sur appareil réel, analyse du lien testée. **Reste** : test mDNS sur de vrais appareils, test avec un vrai compte, point x264 |

M1 dépend de M4 (il faut un appareil à contacter) : il passe **après** l'application Android. M2 et M3 avancent d'abord.

## 7. Tests

* PC : client de test scripté (appairage, état, commandes, synchronisation), tests unitaires du protocole.
* Android : émulateurs téléphone et **Android TV** (Hyper-V disponible), tests unitaires (analyseurs Xtream/M3U/XMLTV sur des échantillons), tests d'interface Compose.
* Réseau : PC ↔ émulateur Android via redirection de port ; le test réel Wi-Fi nécessite un vrai appareil.

## 8. Risques et points à auditer

| Risque | Mesure |
|---|---|
| **Licences libVLC Android** (certains modules VLC sont GPL) | Refaire l'audit fait sur Windows (plugins LGPL uniquement) avant toute distribution |
| **Android TV : navigation au focus** | Compose pour TV, tests systématiques à la télécommande sur l'émulateur TV |
| **Pare-feu / mDNS bloqué** (réseaux invités, isolation Wi-Fi) | Saisie manuelle de l'adresse IP + QR code d'appairage (le PC n'ouvre aucun port entrant) |
| **Identifiants IPTV** qui circulent sur le réseau | TLS épinglé, appairage par PIN, synchronisation des comptes **opt-in**, jamais journalisés |
| **Un seul flux autorisé par abonnement** | Une seule lecture à la fois par compte ; « envoyer vers » remplace la lecture en cours |
| **Distribution** | APK installé à la main (usage personnel) ; Google Play demanderait un compte développeur et une revue de la politique IPTV |

## 9. Outils et téléchargements (confirmés un par un)

Étape 1 (~460 Mo) : JDK 17 Temurin (182 Mo), outils en ligne de commande Android 23.0 (148 Mo), platform-tools (8 Mo), plateforme Android 36 (63 Mo), build-tools 37.0.0 (58 Mo).
Étape 2 (~3 Go) : émulateur (259 Mo), image **Android TV** API 36 x86_64 (944 Mo), image téléphone API 36 x86_64 (1,8 Go).
Au premier build : distribution Gradle (~130 Mo) et dépendances Maven (Compose, libVLC…).

## 10. Notes techniques (jalon M0)

* **Versions AndroidX volontairement rétrogradées** (Compose BOM 2025.10.01, core 1.17.0, activity 1.11.0, lifecycle 2.9.4…) : les versions de fin 2026 exigent compileSdk 37, or seule la plateforme 36 est installée. Pour les remonter : installer platforms;android-37.0 (64 Mo) et passer compileSdk à 37.
* **libVLC** : libvlc-all:3.7.7 (moteur 3.0.25). LibVLC.version() est statique. La branche 4.x est encore en préversion.
* **Plugin Android 9.5.1 / Kotlin 2.4.21 / Gradle 9.9.1** : compilent sans réglage particulier.
* Taille de l'APK de debug : 164 Mo (3 architectures + libVLC) ; une version 
elease avec séparation par ABI sera bien plus petite.


* **M2 — test sans compte réel** : 	ools/mock-xtream/server.py (identifiants demo / demo, port 8099, joignable depuis l'émulateur à http://10.0.2.2:8099) simule l'API Xtream ; les flux sont redirigés vers un flux HLS public de test.
* **Cache et chiffrement** : catalogue en JSON compressé (rafraîchi après 12 h) ; comptes chiffrés AES-256-GCM avec une clé du Keystore Android (jamais exportable).
* **Limite connue** : la langue audio par piste n'a pas pu être testée sur Android (le flux de test n'a qu'une piste) ; la logique est identique à celle validée sur Windows.

### Ajout (2026-11-17) : configuration d'un nouvel appareil par QR code

* **Connexion simplifiée** : au premier lancement, « Se connecter avec un autre appareil » (scan du QR code ou saisie de `ip:port` + code) récupère comptes, réglages, favoris et progression depuis le PC (fenêtre « Partager ma configuration ») ou depuis un autre appareil Android (Réglages ▸ « Afficher un QR code pour configurer un autre appareil »).
* **TV** : au premier lancement, l'écran « Configurer cette TV » affiche un QR code ; le téléphone le scanne (Télécommande ▸ Scanner un QR code) et envoie sa configuration. Protocole : §9 de `protocole-controle-v1.md`.
* **Exception à la règle « le PC n'écoute rien »** : le PC ouvre un serveur éphémère **uniquement** pendant que la fenêtre de partage est affichée, avec un code à usage unique.
* Testé : 5 tests unitaires de l'offre ; transfert réel émulateur ← serveur de partage PC (appairage par code, compte + favori + réglages + historique transférés, catalogue chargé, lecture https). **Non testé** : lecture du QR code par la caméra, écran de configuration TV, fenêtre WPF « Partager » (affichage), pare-feu Windows.
* **libVLC recompilée en LGPLv3** (`tools/build-libvlc-lgpl.sh`, sources dans `android/libvlc-lgpl/`) : plus de x264 ni de composants GPL, TLS/https conservé. `--license a` (LGPLv2) retirait aussi TLS.

