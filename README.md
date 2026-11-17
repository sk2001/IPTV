<p align="center"><img src="assets/logo.png" alt="Logo IPTV Player" width="120"></p>

# IPTV Player

Lecteur IPTV avec **VLC embarqué** : application **Windows** autonome (WPF / .NET 9, LibVLCSharp) et application **Android / Android TV** (Kotlin, Jetpack Compose, libVLC), qui peuvent se piloter et se synchroniser sur le réseau local.

| Plateforme | Dossier | Compiler / installer |
|---|---|---|
| Windows | [`desktop/`](desktop/README.md) | [`desktop/README.md`](desktop/README.md) |
| Android / Android TV | [`android/`](android/README.md) | [`android/README.md`](android/README.md) |

## Fonctionnalités

- **TV en direct « à la Plex »** : l'onglet *TV en direct* s'ouvre sur le **guide en grille** ; un bandeau affiche l'émission sélectionnée (chaîne, « à l'antenne » / « à suivre », horaire, progression, description, boutons *Regarder*, favori et *Dernière chaîne*), et la lecture en cours reste visible dans un **mini-lecteur** à droite du bandeau (clic = plein format, `Échap` = retour au guide). Touche `Q` : retour à la chaîne précédente. Les chaînes sans programme restent listées (ligne « aucune information »).
- **Android / Android TV** : même écran *Live* (bandeau, guide en grille, mini-lecteur) pour téléphone, tablette et TV (détails : `android/README.md`).
- **Synchronisation permanente** : les appareils liés (PC et appareils Android) restent à jour automatiquement (toutes les 30 s et après chaque changement) : comptes (appareils liés par code / QR code, ou option), favoris, historique et progression, réglages. Désactivable dans Réglages ▸ Télécommande.
- Comptes multiples : **Xtream Codes** (serveur + identifiants, ou collage direct d'une URL `get.php?username=…&password=…`) et **playlists M3U** (URL ou fichier local), avec bouton *Tester*.
- Mots de passe chiffrés avec DPAPI (`%AppData%\IptvPlayer\accounts.json`).
- Live, Films, Séries (choix de la saison / de l'épisode), catégories, recherche instantanée, favoris.
- **Guide TV** : grille horaire (chaînes × programmes) avec trait « maintenant », infobulle de description, navigation ±2 h (boutons, `←` `→`, Maj + molette), filtre par catégorie et recherche, double-clic pour regarder. La liste des chaînes affiche aussi l'émission en cours avec sa progression. Source : `xmltv.php` (Xtream) ou `url-tvg` / `x-tvg-url` (M3U, URL ou fichier local) ; rapprochement par `tvg-id` puis par nom de chaîne ; mis en cache 6 h.
- **Enregistrement** : bouton ● du lecteur (ou touche `R`) pour enregistrer le flux en cours en `.ts` (VLC écrit le fichier tout en l'affichant : une seule connexion au fournisseur). Badge `REC` avec durée et taille. L'onglet *Enreg.* liste les fichiers (lire, supprimer, ouvrir ou changer le dossier ; par défaut `Vidéos\IPTV Recordings`). Changer de chaîne ou quitter arrête l'enregistrement (confirmation à la fermeture).
- **Langue de l'interface : anglais (par défaut) ou français**, au choix dans les Paramètres, appliquée immédiatement (menus, messages, dates). Les textes sont dans `desktop/Localization/Loc.cs` : toute nouvelle chaîne s'y ajoute en anglais **et** en français (`{loc:T clé}` en XAML, `Loc.T("clé")` en C#).
- **Licences consultables dans l'appli** (Paramètres → À propos) : les fichiers Markdown (`.md`) sont rendus mis en forme (titres, listes, tableaux, code, liens) par un moteur intégré sans dépendance ; les autres fichiers (LICENSE, texte LGPL) restent en texte brut. Les notices existent en français et en anglais.
- **Paramètres** (engrenage de la barre latérale) : langue audio préférée pour les films et séries (anglais par défaut), langue des sous-titres, langue audio des chaînes live (« Auto » par défaut), reprise de lecture, effacement de l'historique. La langue est transmise à VLC puis complétée en choisissant la piste dont le nom contient la langue (ex. « English »), pour les fournisseurs qui ne renseignent pas les métadonnées.
- **Historique de visionnage** (films et séries, par compte, `history.json`) : catégorie « ↺ Historique » dans Films / Séries (`Suppr` retire un élément), mention « ✓ Vu » / « ▶ 34 % » avec barre de progression dans les listes, **reprise automatique** là où l'on s'est arrêté, et pour les séries : épisodes vus / entamés dans la liste des épisodes, épisode suivant présélectionné, bouton « Marquer vu / non vu ». Un titre est considéré vu à 92 %.
- Lecteur VLC : barre de progression (VOD), volume, muet, pistes audio / sous-titres, plein écran.
- Raccourcis : `Espace` pause · `F` / `F11` plein écran · `Échap` quitter le plein écran · `M` muet · `↑ ↓` volume · `← →` ±10 s · double-clic sur la vidéo = plein écran.
- Performances : catalogue mis en cache (JSON gzip) → affichage instantané au lancement, rafraîchi en arrière-plan ; listes virtualisées (des dizaines de milliers de chaînes) ; décodage matériel VLC ; initialisation de VLC en arrière-plan.

## Compiler

Tout se lance depuis la racine avec **`build.ps1`** (PowerShell) :

```powershell
./build.ps1                          # tout : Windows + Android
./build.ps1 desktop                  # seulement Windows  -> dist\desktop\IptvPlayer.exe
./build.ps1 android                  # seulement Android  -> dist\android\*.apk
./build.ps1 android -AndroidVariant release   # APK signé (clé : android\keystore.properties)
```

* **Windows** : un seul exécutable `dist\desktop\IptvPlayer.exe` (~186 Mo, runtime .NET, WPF et VLC embarqués). Prérequis : .NET 9 SDK. Détails, installation et données : [`desktop/README.md`](desktop/README.md).
* **Android** : un APK par architecture dans `dist\android\`. Par défaut, APK de test (debug) ; APK signé (release) si `android\keystore.properties` existe. Prérequis (JDK 17, SDK Android), installation sur téléphone / TV / émulateur, libVLC en LGPL : [`android/README.md`](android/README.md).
* Une cible qui échoue n'empêche pas l'autre de se compiler ; le code de sortie est 1 si l'une a échoué. `Get-Help ./build.ps1` affiche toutes les options.

## Licences

* Code d'IPTV Player : **MIT** (`LICENSE`).
* Composants tiers : **libVLC / LibVLCSharp (LGPL-2.1+)**, CommunityToolkit.Mvvm, ProtectedData, runtime .NET et WPF (MIT) — détail, obligations et
  textes dans [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) et `LICENSES/` (aussi dans l'appli : Paramètres → À propos).
* Le moteur VLC embarqué **ne contient que des plugins LGPL** : `build.ps1` exclut automatiquement les plugins GPL (x264, lua, etc.).
* Remplacer libVLC (exigence LGPL) : placer un dossier `libvlc` (avec `libvlc.dll`, `libvlccore.dll`, `plugins\`) à côté de `IptvPlayer.exe`.
* **Android** : libVLC y est recompilée en **LGPLv3** (sans x264 ni autre composant GPL) à partir des sources de VideoLAN ; composants Apache-2.0 (AndroidX, Compose, Kotlin, Coil, ZXing). Détail : `android/app/src/main/assets/THIRD_PARTY_NOTICES.md` (affiché dans l'appli). Windows : QRCoder (MIT) pour le QR code de partage.

## Logo

Source : `assets/logo.svg`. Les fichiers `.ico` / `.png` de l'application Windows (`desktop/Assets/`) se régénèrent avec
`powershell.exe -STA -File tools\make-icon.ps1` (à relancer si le dessin change dans le script) ; ceux d'Android avec `tools\make-android-icons.ps1`.

## Contrôle d'appareils Android

Réglages ▸ « Télécommande… » : découvre (mDNS) ou accepte une adresse, appaire un téléphone / une TV Android par PIN, puis le pilote ; le bouton « Envoyer vers » du lecteur lui transmet la lecture en cours. Le PC n'accepte aucune connexion entrante, sauf pendant la fenêtre « Partager ma configuration » (serveur éphémère + QR code pour configurer un nouvel appareil Android). Synchronisation des favoris / historique / réglages (et comptes, en option) depuis cette même fenêtre. Protocole : `docs/protocole-controle-v1.md`.
