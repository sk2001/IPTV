<p align="center"><img src="assets/logo.png" alt="Logo IPTV Player" width="120"></p>

# IPTV Player

Lecteur IPTV Windows autonome (WPF / .NET 9) avec **VLC embarqué** (LibVLCSharp).

## Fonctionnalités

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

## Compiler

```powershell
./build.ps1
```

Produit **un seul fichier** : `dist\IptvPlayer.exe` (~186 Mo) avec le runtime .NET, WPF et le moteur VLC embarqués.
Au premier lancement, VLC est extrait (quelques secondes) dans `%LocalAppData%\IptvPlayer\libvlc-<version>` ; les lancements suivants sont immédiats.

## Licences

* Code d'IPTV Player : **MIT** (`LICENSE`).
* Composants tiers : **libVLC / LibVLCSharp (LGPL-2.1+)**, CommunityToolkit.Mvvm, ProtectedData, runtime .NET et WPF (MIT) — détail, obligations et
  textes dans [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) et `LICENSES/` (aussi dans l'appli : Paramètres → À propos).
* Le moteur VLC embarqué **ne contient que des plugins LGPL** : `build.ps1` exclut automatiquement les plugins GPL (x264, lua, etc.).
* Remplacer libVLC (exigence LGPL) : placer un dossier `libvlc` (avec `libvlc.dll`, `libvlccore.dll`, `plugins\`) à côté de `IptvPlayer.exe`.

## Logo

Source : `assets/logo.svg`. Les fichiers `.ico` / `.png` de l'application se régénèrent avec
`powershell.exe -STA -File tools\make-icon.ps1` (à relancer si le dessin change dans le script).

## Données utilisateur

`%AppData%\IptvPlayer\` : `accounts.json`, `state.json` (favoris, volume, dernier compte, préférences de langue), `history.json` (historique de visionnage), `cache\` (catalogues). `%LocalAppData%\IptvPlayer\libvlc-*` : moteur VLC extrait (les anciennes versions inutilisées sont supprimées automatiquement au lancement, en arrière-plan), `%LocalAppData%\IptvPlayer\img\` : miniatures des logos.

