<p align="center"><img src="assets/logo.png" alt="Logo IPTV Player" width="120"></p>

# IPTV Player

Lecteur IPTV Windows autonome (WPF / .NET 9) avec **VLC embarqué** (LibVLCSharp).

## Fonctionnalités

- Comptes multiples : **Xtream Codes** (serveur + identifiants, ou collage direct d'une URL `get.php?username=…&password=…`) et **playlists M3U** (URL ou fichier local), avec bouton *Tester*.
- Mots de passe chiffrés avec DPAPI (`%AppData%\IptvPlayer\accounts.json`).
- Live, Films, Séries (choix de la saison / de l'épisode), catégories, recherche instantanée, favoris.
- **Guide TV** : grille horaire (chaînes × programmes) avec trait « maintenant », infobulle de description, navigation ±2 h (boutons, `←` `→`, Maj + molette), filtre par catégorie et recherche, double-clic pour regarder. La liste des chaînes affiche aussi l'émission en cours avec sa progression. Source : `xmltv.php` (Xtream) ou `url-tvg` / `x-tvg-url` (M3U, URL ou fichier local) ; rapprochement par `tvg-id` puis par nom de chaîne ; mis en cache 6 h.
- **Enregistrement** : bouton ● du lecteur (ou touche `R`) pour enregistrer le flux en cours en `.ts` (VLC écrit le fichier tout en l'affichant : une seule connexion au fournisseur). Badge `REC` avec durée et taille. L'onglet *Enreg.* liste les fichiers (lire, supprimer, ouvrir ou changer le dossier ; par défaut `Vidéos\IPTV Recordings`). Changer de chaîne ou quitter arrête l'enregistrement (confirmation à la fermeture).
- Lecteur VLC : barre de progression (VOD), volume, muet, pistes audio / sous-titres, plein écran.
- Raccourcis : `Espace` pause · `F` / `F11` plein écran · `Échap` quitter le plein écran · `M` muet · `↑ ↓` volume · `← →` ±10 s · double-clic sur la vidéo = plein écran.
- Performances : catalogue mis en cache (JSON gzip) → affichage instantané au lancement, rafraîchi en arrière-plan ; listes virtualisées (des dizaines de milliers de chaînes) ; décodage matériel VLC ; initialisation de VLC en arrière-plan.

## Compiler

```powershell
./build.ps1
```

Produit **un seul fichier** : `dist\IptvPlayer.exe` (~186 Mo) avec le runtime .NET, WPF et le moteur VLC embarqués.
Au premier lancement, VLC est extrait (quelques secondes) dans `%LocalAppData%\IptvPlayer\libvlc-<version>` ; les lancements suivants sont immédiats.

## Logo

Source : `assets/logo.svg`. Les fichiers `.ico` / `.png` de l'application se régénèrent avec
`powershell.exe -STA -File tools\make-icon.ps1` (à relancer si le dessin change dans le script).

## Données utilisateur

`%AppData%\IptvPlayer\` : `accounts.json`, `state.json` (favoris, volume, dernier compte), `cache\` (catalogues). `%LocalAppData%\IptvPlayer\libvlc-*` : moteur VLC extrait.

