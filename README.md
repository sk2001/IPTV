<p align="center"><img src="assets/logo.png" alt="Logo IPTV Player" width="120"></p>

# IPTV Player

Lecteur IPTV avec **VLC embarqué** : application **Windows** autonome (WPF / .NET 9, LibVLCSharp) et application **Android / Android TV** (Kotlin, Jetpack Compose, libVLC), qui peuvent se piloter et se synchroniser sur le réseau local.

| Plateforme | Dossier | Compiler / installer |
|---|---|---|
| Windows | `src/IptvPlayer` | [section ci-dessous](#compiler-et-installer-windows) |
| Android / Android TV | `android/` | [section ci-dessous](#compiler-et-installer-android) · détails dans [`android/README.md`](android/README.md) |

## Fonctionnalités

- **TV en direct « à la Plex »** : l'onglet *TV en direct* s'ouvre sur le **guide en grille** ; un bandeau affiche l'émission sélectionnée (chaîne, « à l'antenne » / « à suivre », horaire, progression, description, boutons *Regarder*, favori et *Dernière chaîne*), et la lecture en cours reste visible dans un **mini-lecteur** à droite du bandeau (clic = plein format, `Échap` = retour au guide). Touche `Q` : retour à la chaîne précédente. Les chaînes sans programme restent listées (ligne « aucune information »).
- **Android / Android TV** : même écran *Live* (bandeau, guide en grille, mini-lecteur) pour téléphone, tablette et TV (détails : `android/README.md`).
- **Synchronisation permanente** : les appareils liés (PC et appareils Android) restent à jour automatiquement (toutes les 30 s et après chaque changement) : comptes (appareils liés par code / QR code, ou option), favoris, historique et progression, réglages. Désactivable dans Réglages ▸ Télécommande.
- Comptes multiples : **Xtream Codes** (serveur + identifiants, ou collage direct d'une URL `get.php?username=…&password=…`) et **playlists M3U** (URL ou fichier local), avec bouton *Tester*.
- Mots de passe chiffrés avec DPAPI (`%AppData%\IptvPlayer\accounts.json`).
- Live, Films, Séries (choix de la saison / de l'épisode), catégories, recherche instantanée, favoris.
- **Guide TV** : grille horaire (chaînes × programmes) avec trait « maintenant », infobulle de description, navigation ±2 h (boutons, `←` `→`, Maj + molette), filtre par catégorie et recherche, double-clic pour regarder. La liste des chaînes affiche aussi l'émission en cours avec sa progression. Source : `xmltv.php` (Xtream) ou `url-tvg` / `x-tvg-url` (M3U, URL ou fichier local) ; rapprochement par `tvg-id` puis par nom de chaîne ; mis en cache 6 h.
- **Enregistrement** : bouton ● du lecteur (ou touche `R`) pour enregistrer le flux en cours en `.ts` (VLC écrit le fichier tout en l'affichant : une seule connexion au fournisseur). Badge `REC` avec durée et taille. L'onglet *Enreg.* liste les fichiers (lire, supprimer, ouvrir ou changer le dossier ; par défaut `Vidéos\IPTV Recordings`). Changer de chaîne ou quitter arrête l'enregistrement (confirmation à la fermeture).
- **Langue de l'interface : anglais (par défaut) ou français**, au choix dans les Paramètres, appliquée immédiatement (menus, messages, dates). Les textes sont dans `src/IptvPlayer/Localization/Loc.cs` : toute nouvelle chaîne s'y ajoute en anglais **et** en français (`{loc:T clé}` en XAML, `Loc.T("clé")` en C#).
- **Licences consultables dans l'appli** (Paramètres → À propos) : les fichiers Markdown (`.md`) sont rendus mis en forme (titres, listes, tableaux, code, liens) par un moteur intégré sans dépendance ; les autres fichiers (LICENSE, texte LGPL) restent en texte brut. Les notices existent en français et en anglais.
- **Paramètres** (engrenage de la barre latérale) : langue audio préférée pour les films et séries (anglais par défaut), langue des sous-titres, langue audio des chaînes live (« Auto » par défaut), reprise de lecture, effacement de l'historique. La langue est transmise à VLC puis complétée en choisissant la piste dont le nom contient la langue (ex. « English »), pour les fournisseurs qui ne renseignent pas les métadonnées.
- **Historique de visionnage** (films et séries, par compte, `history.json`) : catégorie « ↺ Historique » dans Films / Séries (`Suppr` retire un élément), mention « ✓ Vu » / « ▶ 34 % » avec barre de progression dans les listes, **reprise automatique** là où l'on s'est arrêté, et pour les séries : épisodes vus / entamés dans la liste des épisodes, épisode suivant présélectionné, bouton « Marquer vu / non vu ». Un titre est considéré vu à 92 %.
- Lecteur VLC : barre de progression (VOD), volume, muet, pistes audio / sous-titres, plein écran.
- Raccourcis : `Espace` pause · `F` / `F11` plein écran · `Échap` quitter le plein écran · `M` muet · `↑ ↓` volume · `← →` ±10 s · double-clic sur la vidéo = plein écran.
- Performances : catalogue mis en cache (JSON gzip) → affichage instantané au lancement, rafraîchi en arrière-plan ; listes virtualisées (des dizaines de milliers de chaînes) ; décodage matériel VLC ; initialisation de VLC en arrière-plan.

## Compiler et installer (Windows)

Prérequis : **.NET 9 SDK** et Windows 10/11 x64.

```powershell
./build.ps1                 # -> dist\IptvPlayer.exe
```

Produit **un seul fichier** : `dist\IptvPlayer.exe` (~186 Mo) avec le runtime .NET, WPF et le moteur VLC embarqués. Aucune installation : copier le fichier où l'on veut et le lancer.
Au premier lancement, VLC est extrait (quelques secondes) dans `%LocalAppData%\IptvPlayer\libvlc-<version>` ; les lancements suivants sont immédiats.
Pour désinstaller : supprimer l'exécutable, `%AppData%\IptvPlayer` (données) et `%LocalAppData%\IptvPlayer` (moteur VLC extrait).

Pour compiler sans produire l'exécutable unique (développement) : `dotnet build src/IptvPlayer/IptvPlayer.csproj -o <dossier-temporaire>`.
La première fois que l'on utilise « Partager ma configuration », Windows peut demander d'autoriser l'application sur les réseaux privés : accepter.

## Compiler et installer (Android)

Résumé (détails, prérequis, émulateur et dépannage : [`android/README.md`](android/README.md)) :

```powershell
. .\tools\android-env.ps1          # une fois par fenêtre PowerShell
cd android

# --- APK de test (signé avec la clé de débogage) ---
.\gradlew.bat assembleDebug
adb install -r app\build\outputs\apk\debug\app-arm64-v8a-debug.apk     # téléphone / TV réels (débogage USB activé)
adb install -r app\build\outputs\apk\debug\app-x86_64-debug.apk        # émulateur

# --- APK de distribution : créer d'abord une clé et android\keystore.properties (voir android/README.md §4) ---
.\gradlew.bat assembleRelease
adb install -r app\build\outputs\apk\release\app-arm64-v8a-release.apk

# --- Tests unitaires ---
.\gradlew.bat testDebugUnitTest
```

Si une version signée autrement est déjà installée (debug ↔ release), la désinstaller d'abord (`adb uninstall app.iptvplayer`) ; les appareils déjà appairés devront alors être appairés à nouveau.
**Avant de diffuser un APK**, recompiler libVLC en LGPL (`tools/build-libvlc-lgpl.sh`, sous WSL) : le paquet officiel contient du code GPL (x264) ; voir `android/README.md` §5.

## Licences

* Code d'IPTV Player : **MIT** (`LICENSE`).
* Composants tiers : **libVLC / LibVLCSharp (LGPL-2.1+)**, CommunityToolkit.Mvvm, ProtectedData, runtime .NET et WPF (MIT) — détail, obligations et
  textes dans [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) et `LICENSES/` (aussi dans l'appli : Paramètres → À propos).
* Le moteur VLC embarqué **ne contient que des plugins LGPL** : `build.ps1` exclut automatiquement les plugins GPL (x264, lua, etc.).
* Remplacer libVLC (exigence LGPL) : placer un dossier `libvlc` (avec `libvlc.dll`, `libvlccore.dll`, `plugins\`) à côté de `IptvPlayer.exe`.
* **Android** : libVLC y est recompilée en **LGPLv3** (sans x264 ni autre composant GPL) à partir des sources de VideoLAN ; composants Apache-2.0 (AndroidX, Compose, Kotlin, Coil, ZXing). Détail : `android/app/src/main/assets/THIRD_PARTY_NOTICES.md` (affiché dans l'appli). Windows : QRCoder (MIT) pour le QR code de partage.

## Logo

Source : `assets/logo.svg`. Les fichiers `.ico` / `.png` de l'application se régénèrent avec
`powershell.exe -STA -File tools\make-icon.ps1` (à relancer si le dessin change dans le script).

## Contrôle d'appareils Android

Réglages ▸ « Télécommande… » : découvre (mDNS) ou accepte une adresse, appaire un téléphone / une TV Android par PIN, puis le pilote ; le bouton « Envoyer vers » du lecteur lui transmet la lecture en cours. Le PC n'accepte aucune connexion entrante, sauf pendant la fenêtre « Partager ma configuration » (serveur éphémère + QR code pour configurer un nouvel appareil Android). Synchronisation des favoris / historique / réglages (et comptes, en option) depuis cette même fenêtre. Protocole : `docs/protocole-controle-v1.md`.

## Données utilisateur

`%AppData%\IptvPlayer\` : `accounts.json`, `remote-targets.json` (appareils appairés, jetons chiffrés DPAPI), `state.json` (favoris, volume, dernier compte, préférences de langue), `history.json` (historique de visionnage), `cache\` (catalogues). `%LocalAppData%\IptvPlayer\libvlc-*` : moteur VLC extrait (les anciennes versions inutilisées sont supprimées automatiquement au lancement, en arrière-plan), `%LocalAppData%\IptvPlayer\img\` : miniatures des logos.

